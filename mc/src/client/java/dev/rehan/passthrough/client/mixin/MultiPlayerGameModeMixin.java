package dev.rehan.passthrough.client.mixin;

import dev.rehan.passthrough.client.HostState;
import net.minecraft.client.Minecraft;
import net.minecraft.client.multiplayer.MultiPlayerGameMode;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.world.level.block.Blocks;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** The host's ground (barriers) can be built on but never broken: only the player's own blocks break. */
@Mixin(MultiPlayerGameMode.class)
abstract class MultiPlayerGameModeMixin {
	private static boolean passthrough$isGround(final BlockPos pos) {
		Minecraft minecraft = Minecraft.getInstance();
		return HostState.frame() != null && minecraft.level != null && minecraft.level.getBlockState(pos).is(Blocks.BARRIER);
	}

	@Inject(method = "startDestroyBlock", at = @At("HEAD"), cancellable = true)
	private void passthrough$start(final BlockPos pos, final Direction direction, final CallbackInfoReturnable<Boolean> cir) {
		if (passthrough$isGround(pos)) {
			cir.setReturnValue(false);
		}
	}

	@Inject(method = "continueDestroyBlock", at = @At("HEAD"), cancellable = true)
	private void passthrough$continue(final BlockPos pos, final Direction direction, final CallbackInfoReturnable<Boolean> cir) {
		if (passthrough$isGround(pos)) {
			cir.setReturnValue(false);
		}
	}

	@Inject(method = "destroyBlock", at = @At("HEAD"), cancellable = true)
	private void passthrough$destroy(final BlockPos pos, final CallbackInfoReturnable<Boolean> cir) {
		if (passthrough$isGround(pos)) {
			cir.setReturnValue(false);
		}
	}
}
