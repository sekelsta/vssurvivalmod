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
                // TODO: When could this happen? What to do?
                // Maybe if the world is loaded once without the henbox file, then again with it?
                // Probably give a user-facing error and return, or just crash? Or can we make a new blockentity?
            }
            // Try to take all eggs from nest
            bool anyEggs = false;
            for (int i = 0; i < nest.Inventory.Count; ++i)
            {
                if (!nest.Inventory[i].Empty)
                {
                    string audit = nest.Inventory[i].Itemstack.Collectible?.Code;
                    // TODO: remove attributes, so that the eggs will stack
                    // Assume stack size is 1, because I don't understand inventory transfer code
                    if (nest.Inventory[i].Itemstack.StackSize != 1)
                    {
                        throw new System.Exception("Henbox should only have items stacked to 1!");
                    }
                    if (byPlayer.InventoryManager.TryGiveItemstack(nest.Inventory[i].Itemstack))
                    {
                        ItemStack stack = nest.Inventory[i].TakeOut(1);
                        anyEggs = true;
                        world.Api.Logger.Audit(byPlayer.PlayerName + " took 1x " + audit + " from " + nest.Block.Code + " at " + nest.Pos);
                    }
                    else
                    {
                        // For some reason trying and failing to give itemstack changes the stack size to 0
                        nest.Inventory[i].Itemstack.StackSize = 1;
                    }
                    // If it doesn't fit, leave it in the nest
                }
/*
                if (!nest.Inventory[i].Empty)
                {
                    string audit = nest.Inventory[i].Itemstack.Collectible?.Code;
                    int quantity = nest.Inventory[i].Itemstack.StackSize;
                    // TODO: Test that this does the right thing if the player can only fit part of the stack
                    if (byPlayer.InventoryManager.TryGiveItemstack(nest.Inventory[i].Itemstack))
                    {
                        if (quantity == stack.StackSize)
                        {
                            ItemStack stack = nest.Inventory[i].TakeOutWhole();
                        }
                        anyEggs = true;
                        world.Api.Logger.Audit(byPlayer.PlayerName + " took " + quantity - stack.StackSize + "x " + audit + " from " + Block.Code + " at " + Pos);
                    }
                    else
                    {
                        // For some reason trying and failing to give itemstack changes the stack size to 0
                        nest.Inventory[i].Itemstack.StackSize = quantity;
                    }
                    // If it doesn't fit, leave it in the nest
                }
*/
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
