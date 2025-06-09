using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Vintagestory.GameContent
{
    public class BehaviorCollectFrom : BlockBehavior
    {
        public BehaviorCollectFrom(Block block) : base(block)
        {
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            {
                return false;
            }

            BlockEntityHenBox nest = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityHenBox;
            if (nest == null)
            {
                // Could happen if the world is loaded without the block definition due to mods
                world.Api.Logger.Warning("Nest box at " + blockSel.Position + " has no associated BlockEntityHenBox, try breaking the block and placing it again.");
                return false;
            }

            // TEST CODE. TODO: Remove
            ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (!slot.Empty) {
                //ItemStackMoveOperation op = new ItemStackMoveOperation(nest.Api.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge);
                ItemStackMoveOperation op = new ItemStackMoveOperation(nest.Api.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, 1);
                // Place egg in nest
                for (int i = 0; i < nest.Inventory.Count; ++i) {
                    if (nest.Inventory[i].Empty) {
                        AssetLocation sound = slot.Itemstack?.Block?.Sounds?.Place;
                        AssetLocation itemPlaced = slot.Itemstack?.Collectible?.Code;
                        if (slot.TryPutInto(nest.Inventory[i], ref op) > 0) {
                            handling = EnumHandling.PreventSubsequent;
                            world.Api.Logger.Notification("sekdebug slot=" + i + " item=" + itemPlaced + " quantity=" + nest.Inventory[i].Itemstack.StackSize + " (placed)");
                            return true;
                        }
                    }
                }
                return false;
            }
            //// END TEST CODE

            // Try to take all eggs from nest
            bool anyEggs = false;
            for (int i = 0; i < nest.Inventory.Count; ++i)
            {
                if (!nest.Inventory[i].Empty)
                {
                    string audit = nest.Inventory[i].Itemstack.Collectible?.Code;
                    int quantity = nest.Inventory[i].Itemstack.StackSize;
                    world.Api.Logger.Notification("sekdebug slot=" + i + " item=" + audit + " quantity=" + quantity + " (before)");
                    // TODO: Test that this does the right thing if the player can only fit part of the stack
                    if (byPlayer.InventoryManager.TryGiveItemstack(nest.Inventory[i].Itemstack))
                    {
                        int taken = quantity - (nest.Inventory[i].Itemstack?.StackSize ?? 0);
                        world.Api.Logger.Notification("sekdebug slot=" + i + " item=" + nest.Inventory[i].Itemstack?.Collectible?.Code + " quantity=" + nest.Inventory[i].Itemstack?.StackSize + " (during)");
                        if (nest.Inventory[i].Itemstack != null && nest.Inventory[i].Itemstack.StackSize == 0)
                        {
                            // Otherwise eggs with stack size 0 will still be displayed and still occupy a slot
                            nest.Inventory[i].Itemstack = null;
                        }
                        else if (quantity == nest.Inventory[i].Itemstack.StackSize)
                        {
                            ItemStack stack = nest.Inventory[i].TakeOutWhole();
                        }

                        anyEggs = true;
                        world.Api.Logger.Audit(byPlayer.PlayerName + " took " + taken + "x " + audit + " from " + nest.Block.Code + " at " + nest.Pos);
                        // TODO: Update hotbar slots?
                    }
                    else
                    {
                        world.Api.Logger.Notification("sekdebug trygiveitemstack returned false");
                        // For some reason trying and failing to give itemstack changes the stack size to 0
                        nest.Inventory[i].Itemstack.StackSize = quantity;
                    }
                    nest.Inventory.DidModifyItemSlot(nest.Inventory[i]);
                    world.Api.Logger.Notification("sekdebug slot=" + i + " item=" + nest.Inventory[i].Itemstack?.Collectible?.Code + " quantity=" + nest.Inventory[i].Itemstack?.StackSize + " (after)");
                    // If it doesn't fit, leave it in the nest
                }
            }

            if (anyEggs) {
                world.PlaySoundAt(new AssetLocation("sounds/player/collect"), blockSel.Position.X, blockSel.Position.Y, blockSel.Position.Z, byPlayer);
            }
            return anyEggs;
        }

        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            if (blockSel == null) return false;

            (byPlayer as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemAttack);
            return true;
        }

    }
}
