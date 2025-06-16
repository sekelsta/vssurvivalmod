using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    /** Mechanics
     * 
     * 1.  / Any well-fed hen - i.e. ready to lay - will activate task AITaskSeekBlockAndLay
     * 2.  / Once sitting on the henbox for 5 real seconds, the hen will first attempt to lay an egg in the henbox
     * 3.  / If the hen can lay an egg (fewer than 3 eggs currently) it does so; makes an egg laying sound and activates another AITask.  TODO: we could add a flapping animation
     * 4. / The egg will be fertile (it will have a chickCode) if there was a male nearby; otherwise it will be infertile; eggs added by a player are always infertile (for now).  [TODO: track individual egg items' fertility and parentage so that players can micro-manage]
     * 5.  / If the hen cannot lay an egg (henbox is full of 3 eggs already), the hen becomes "broody" and will sit on the eggs for a long time (around three quarters of a day)
     * 6.  / That broody hen or another broody hen will continue returning to the henbox and sitting on the eggs until they eventually hatch
     * 7.  / HenBox BlockEntity tracks how long a hen (any hen) has sat on the eggs warming them - as set in the chicken-hen JSON it needs 5 in-game days
     * 8.  / When the eggs have been warmed for long enough they hatch: chicks are spawned and the henbox reverts to empty
     * 
     * HenBox tracks the parent entity and the generation of each egg separately => in future could have 1 duck egg in a henbox for example, so that 1 duckling hatches and 2 hen chicks
     */

    public class BlockEntityHenBox : BlockEntityDisplay, IAnimalNest
    {
        protected InventoryGeneric inventory;
        public override InventoryBase Inventory => inventory;
        public string inventoryClassName = "nestbox";
        public override string InventoryClassName => inventoryClassName;

        public Vec3d Position => Pos.ToVec3d().Add(0.5, 0.5, 0.5);
        public string Type => "nest";

        public Entity occupier;

        protected int[] parentGenerations = new int[10];
        protected AssetLocation[] chickNames = new AssetLocation[10];
        protected double timeToIncubate;
        protected double occupiedTimeLast;
        protected bool IsOccupiedClientside = false;


        public BlockEntityHenBox()
        {
            container = new ConstantPerishRateContainer(() => Inventory, "inventory");
        }


        public virtual bool IsSuitableFor(Entity entity)
        {
            return entity is EntityAgent && entity.Code.Path == "chicken-hen";
        }

        public bool Occupied(Entity entity)
        {
            return occupier != null && occupier != entity;
        }

        public virtual void SetOccupier(Entity entity)
        {
            if (occupier == entity)
            {
                return;
            }
            occupier = entity;
            MarkDirty();
        }

        public virtual float DistanceWeighting => 2 / (CountEggs() + 2);


        public virtual bool TryAddEgg(Entity entity, string chickCode, double incubationTime)
        {
            for (int i = 0; i < inventory.Count; ++i) {
                if (inventory[i].Empty) {
                    inventory[i].Itemstack = MakeEggItem(entity, chickCode, incubationTime, i);
                    inventory.DidModifyItemSlot(inventory[i]);
                    timeToIncubate = 0;
                    return true;
                }
            }
            if (timeToIncubate == 0)
            {
                timeToIncubate = incubationTime;
                occupiedTimeLast = entity.World.Calendar.TotalDays;
                MarkDirty();
            }
            return false;
        }

        protected ItemStack MakeEggItem(Entity entity, string chickCode, double incubationTime, int i)
        {
            ItemStack eggStack;
            JsonItemStack[] eggTypes = entity.Properties.Attributes?["eggTypes"].AsArray<JsonItemStack>();
            if (eggTypes == null)
            {
                string fallbackCode = "game:egg-chicken-raw";
                entity.Api.Logger.Warning("No egg type specified for entity " + entity.Code + ", falling back to " + fallbackCode);
                eggStack = new ItemStack(entity.World.GetItem(fallbackCode));
            }
            else
            {
                JsonItemStack jsonEgg = eggTypes[entity.World.Rand.Next(eggTypes.Length)];
                if (!jsonEgg.Resolve(entity.World, null, false))
                {
                    entity.Api.Logger.Warning("Failed to resolve egg " + jsonEgg.Type + " with code " + jsonEgg.Code + " for entity " + entity.Code);
                    return null;
                }
                eggStack = new ItemStack(jsonEgg.ResolvedItemstack.Collectible);
            }

            parentGenerations[i] = entity.WatchedAttributes.GetInt("generation", 0);
            chickNames[i] = chickCode == null ? null : entity.Code.CopyWithPath(chickCode);
            MarkDirty();

            return eggStack;
        }

        public int CountEggs()
        {
            int count = 0;
            for (int i = 0; i < inventory.Count; ++i)
            {
                if (!inventory[i].Empty)
                {
                    ++count;
                }
            }
            return count;
        }

        protected virtual void On1500msTick(float dt)
        {
            if (timeToIncubate == 0) return;

            double newTime = Api.World.Calendar.TotalDays;
            if (occupier != null && occupier.Alive)   //Does this need a more sophisticated check, i.e. is the occupier's position still here?  (Also do we reset the occupier variable to null if save and re-load?)
            {
                if (newTime > occupiedTimeLast)
                {
                    timeToIncubate -= newTime - occupiedTimeLast;
                    this.MarkDirty();
                }
            }
            occupiedTimeLast = newTime;

            if (timeToIncubate <= 0)
            {
                timeToIncubate = 0;
                Random rand = Api.World.Rand;

                for (int i = 0; i < inventory.Count; ++i)
                {
                    if (inventory[i].Empty)
                    {
                        continue;
                    }
                    AssetLocation chickName = chickNames[i];
                    if (chickName == null) continue;
                    int generation = parentGenerations[i];

                    EntityProperties childType = Api.World.GetEntityType(chickName);
                    if (childType == null) continue;
                    Entity childEntity = Api.World.ClassRegistry.CreateEntity(childType);
                    if (childEntity == null) continue;

                    childEntity.ServerPos.SetFrom(new EntityPos(this.Position.X + (rand.NextDouble() - 0.5f) / 5f, this.Position.Y, this.Position.Z + (rand.NextDouble() - 0.5f) / 5f, (float) rand.NextDouble() * GameMath.TWOPI));
                    childEntity.ServerPos.Motion.X += (rand.NextDouble() - 0.5f) / 200f;
                    childEntity.ServerPos.Motion.Z += (rand.NextDouble() - 0.5f) / 200f;

                    childEntity.Pos.SetFrom(childEntity.ServerPos);
                    childEntity.Attributes.SetString("origin", "reproduction");
                    childEntity.WatchedAttributes.SetInt("generation", generation + 1);
                    Api.World.SpawnEntity(childEntity);

                    inventory[i].Itemstack = null;
                    inventory.DidModifyItemSlot(inventory[i]);
                }
            }
        }


        public override void Initialize(ICoreAPI api)
        {
            inventoryClassName = Block.Attributes?["inventoryClassName"]?.AsString() ?? inventoryClassName;
            int capacity = Block.Attributes?["quantitySlots"]?.AsInt(1) ?? 1;
            if (inventory == null) {
                CreateInventory(capacity, api);
            }
            else if (capacity != inventory.Count) {
                api.Logger.Warning("Nest " + Block.Code + " loaded with " + inventory.Count + " capacity when it should be " + capacity + ".");
                // TODO: Reconsider this - at the very least fill empty slots if oldInv.Count > capacity
                InventoryGeneric oldInv = inventory;
                CreateInventory(capacity, api);
                for (int i = 0; i < capacity && i < oldInv.Count; ++i) {
                    if (!oldInv[i].Empty) {
                        inventory[i].Itemstack = oldInv[i].Itemstack;
                        inventory.DidModifyItemSlot(inventory[i]);
                    }
                }
            }
            base.Initialize(api);

            if (api.Side == EnumAppSide.Server) {
                IsOccupiedClientside = false;
                api.ModLoader.GetModSystem<POIRegistry>().AddPOI(this);
                RegisterGameTickListener(On1500msTick, 1500);
            }
        }

        protected void CreateInventory(int capacity, ICoreAPI api)
        {
            inventory = new InventoryGeneric(capacity, InventoryClassName, Pos?.ToString(), api);
            inventory.Pos = this.Pos;
            inventory.SlotModified += OnSlotModified;
        }

        protected virtual void OnSlotModified(int slot)
        {
            MarkDirty();
        }


        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();

            if (Api.Side == EnumAppSide.Server)
            {
                Api.ModLoader.GetModSystem<POIRegistry>().RemovePOI(this);
            }
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();

            if (Api?.Side == EnumAppSide.Server)
            {
                Api.ModLoader.GetModSystem<POIRegistry>().RemovePOI(this);
            }
        }


        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetDouble("inc", timeToIncubate);
            tree.SetDouble("occ", occupiedTimeLast);
            for (int i = 0; i < 10; i++)
            {
                tree.SetInt("gen" + i, parentGenerations[i]);
                AssetLocation chickName = chickNames[i];
                if (chickName != null) tree.SetString("chick" + i, chickName.ToShortString());
            }
            tree.SetBool("isOccupied", occupier != null && occupier.Alive);
        }


        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            TreeAttribute invTree = (TreeAttribute) tree["inventory"];
            if (inventory == null) {
                int capacity = invTree.GetInt("qslots");
                CreateInventory(capacity, worldForResolving.Api);
            }
            // TODO: Convert from old henbox to new
            base.FromTreeAttributes(tree, worldForResolving);
            timeToIncubate = tree.GetDouble("inc");
            occupiedTimeLast = tree.GetDouble("occ");
            for (int i = 0; i < 10; i++)
            {
                parentGenerations[i] = tree.GetInt("gen" + i);
                string chickName = tree.GetString("chick" + i);
                chickNames[i] = chickName == null ? null : new AssetLocation(chickName);
            }
            IsOccupiedClientside = tree.GetBool("isOccupied");
            RedrawAfterReceivingTreeAttributes(worldForResolving);
        }

        public bool OnInteract(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) 
        {
            if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            {
                return false;
            }

            // TEST CODE. TODO: Remove
            ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
            ItemStackMoveOperation op = new ItemStackMoveOperation(Api.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, 1);
            if (slot.Itemstack != null) {
                // Place egg in nest
                for (int i = 0; i < inventory.Count; ++i) {
                    if (inventory[i].Empty) {
                        AssetLocation sound = slot.Itemstack?.Block?.Sounds?.Place;
                        AssetLocation itemPlaced = slot.Itemstack?.Collectible?.Code;
                        if (slot.TryPutInto(inventory[i], ref op) > 0) {
                            world.Api.Logger.Audit(byPlayer.PlayerName + " put 1x" + itemPlaced + " into " + Block.Code + " at " + Pos);
                            Api.World.PlaySoundAt(sound != null ? sound : new AssetLocation("sounds/player/build"), byPlayer.Entity, byPlayer, true, 16);
                            return true;
                        }
                    }
                }
                return false;
            }
            //// END TEST CODE
            // Try to take all eggs from nest
            bool anyEggs = false;
            for (int i = 0; i < inventory.Count; ++i)
            {
                if (!inventory[i].Empty)
                {
                    string audit = inventory[i].Itemstack.Collectible?.Code;
                    int quantity = inventory[i].Itemstack.StackSize;
/*
                    if (!byPlayer.InventoryManager.TryGiveItemstack(stack))
                    {
                        world.SpawnItemEntity(drop.GetNextItemStack(), blockSel.Position);
                    }
                    inventory[i].Itemstack = null;
                    inventory.DidModifyItemSlot(inventory[i]);*/

                    world.Api.Logger.Notification("sekdebug slot=" + i + " item=" + audit + " quantity=" + quantity + " (before)");
                    // TODO: Test that this does the right thing if the player can only fit part of the stack
                    if (byPlayer.InventoryManager.TryGiveItemstack(inventory[i].Itemstack))
                    {
                        int taken = quantity - (inventory[i].Itemstack?.StackSize ?? 0);
                        world.Api.Logger.Notification("sekdebug slot=" + i + " item=" + inventory[i].Itemstack?.Collectible?.Code + " quantity=" + inventory[i].Itemstack?.StackSize + " (during)");
                        if (inventory[i].Itemstack != null && inventory[i].Itemstack.StackSize == 0)
                        {
                            // Otherwise eggs with stack size 0 will still be displayed and still occupy a slot
                            inventory[i].Itemstack = null;
                        }
                        else if (quantity == inventory[i].Itemstack.StackSize)
                        {
                            ItemStack stack = inventory[i].TakeOutWhole();
                        }

                        anyEggs = true;
                        world.Api.Logger.Audit(byPlayer.PlayerName + " took " + taken + "x " + audit + " from " + Block.Code + " at " + Pos);
                        inventory.DidModifyItemSlot(inventory[i]);
                    }
                    else
                    {
                        world.Api.Logger.Notification("sekdebug trygiveitemstack returned false");
                        // For some reason trying and failing to give itemstack changes the stack size to 0
                        inventory[i].Itemstack.StackSize = quantity;
                    }
                    // If it doesn't fit, leave it in the nest
                }
            }
            if (anyEggs)
            {
                world.PlaySoundAt(new AssetLocation("sounds/player/collect"), blockSel.Position.X, blockSel.Position.Y, blockSel.Position.Z, byPlayer);
            }
            return anyEggs;
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            int eggCount = CountEggs();
            int fertileCount = 0;
            for (int i = 0; i < eggCount; i++) if (chickNames[i] != null) fertileCount++;
            if (fertileCount > 0)
            {
                if (fertileCount > 1)
                    dsc.AppendLine(Lang.Get("{0} fertile eggs", fertileCount));
                else
                    dsc.AppendLine(Lang.Get("1 fertile egg"));

                if (timeToIncubate >= 1.5)
                    dsc.AppendLine(Lang.Get("Incubation time remaining: {0:0} days", timeToIncubate));
                else if (timeToIncubate >= 0.75)
                    dsc.AppendLine(Lang.Get("Incubation time remaining: 1 day"));
                else if (timeToIncubate > 0)
                    dsc.AppendLine(Lang.Get("Incubation time remaining: {0:0} hours", timeToIncubate * 24));

                if (!IsOccupiedClientside && eggCount >= inventory.Count)
                    dsc.AppendLine(Lang.Get("A broody hen is needed!"));
            }
            else if (eggCount > 0)
            {
                dsc.AppendLine(Lang.Get("No eggs are fertilized"));
            }
        }

        protected override float[][] genTransformationMatrices()
        {
            ModelTransform[] transforms = Block.Attributes?["displayTransforms"]?.AsArray<ModelTransform>();
            if (transforms == null)
            {
                capi.Logger.Warning("No display transforms found for " + Block.Code + ", autogenerating placeholders.");
                transforms = new ModelTransform[DisplayedItems];
                for (int i = 0; i < transforms.Length; ++i)
                {
                    // TODO: Consider doing something stupid like having the Z value increase for each one
                    transforms[i] = new ModelTransform();
                }
            }
            if (transforms.Length != DisplayedItems)
            {
                // TODO: What happens in this case? If there are too few transforms, do we crash when the nestbox fills up?
                capi.Logger.Warning("Display transforms for " + Block.Code + " block entity do not match number of displayed items. Items: " + DisplayedItems + ", transforms: " + transforms.Length);
            }

            float[][] tfMatrices = new float[transforms.Length][];
            for (int i = 0; i < transforms.Length; ++i)
            {
                Vec3f off = transforms[i].Translation;
                Vec3f rot = transforms[i].Rotation;
                tfMatrices[i] = new Matrixf()
                    .Translate(off.X, off.Y, off.Z)
                    .Translate(0.5f, 0, 0.5f)
                    .RotateX(rot.X * GameMath.DEG2RAD)
                    .RotateY(rot.Y * GameMath.DEG2RAD)
                    .RotateZ(rot.Z * GameMath.DEG2RAD)
                    .Translate(-0.5f, 0, -0.5f)
                    .Values;
            }
            return tfMatrices;
        }
    }
}
