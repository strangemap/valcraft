package dev.rehan.passthrough.client.mixin;

import dev.rehan.passthrough.client.HostState;
import dev.rehan.passthrough.client.PlayerSync;
import net.minecraft.client.player.LocalPlayer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(LocalPlayer.class)
abstract class LocalPlayerMixin {
	/** After the old position was saved for interpolation and before movement is sent to the server. */
	@Inject(method = "tick", at = @At("HEAD"))
	private void passthrough$followHost(final CallbackInfo ci) {
		PlayerSync.tick((LocalPlayer) (Object) this);
	}

	/** The host's Shift is the sneak (Minecraft's window never sees the key): pose, model and edge logic follow it. */
	@Inject(method = "isCrouching", at = @At("HEAD"), cancellable = true)
	private void passthrough$hostCrouch(final CallbackInfoReturnable<Boolean> cir) {
		HostState.Pose p = HostState.live();
		if (p != null) {
			cir.setReturnValue(p.sneak());
		}
	}

	@Inject(method = "isShiftKeyDown", at = @At("HEAD"), cancellable = true)
	private void passthrough$hostShift(final CallbackInfoReturnable<Boolean> cir) {
		HostState.Pose p = HostState.live();
		if (p != null) {
			cir.setReturnValue(p.sneak());
		}
	}

	/** Minecraft's own tick turns the body (towards the head, the walk): the host's angles win, or he flickers between both. */
	@Inject(method = "tick", at = @At("TAIL"))
	private void passthrough$keepHostRotation(final CallbackInfo ci) {
		PlayerSync.afterTick((LocalPlayer) (Object) this);
	}
}
