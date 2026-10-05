package dev.rehan.passthrough.client.mixin;

import dev.rehan.passthrough.client.PlayerSync;
import net.minecraft.client.player.LocalPlayer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(LocalPlayer.class)
abstract class LocalPlayerMixin {
	/** After the old position was saved for interpolation and before movement is sent to the server. */
	@Inject(method = "tick", at = @At("HEAD"))
	private void passthrough$followHost(final CallbackInfo ci) {
		PlayerSync.tick((LocalPlayer) (Object) this);
	}

	/** Minecraft's own tick turns the body (towards the head, the walk): the host's angles win, or he flickers between both. */
	@Inject(method = "tick", at = @At("TAIL"))
	private void passthrough$keepHostRotation(final CallbackInfo ci) {
		PlayerSync.afterTick((LocalPlayer) (Object) this);
	}
}
