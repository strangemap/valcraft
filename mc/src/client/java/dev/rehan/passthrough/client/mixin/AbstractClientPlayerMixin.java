package dev.rehan.passthrough.client.mixin;

import dev.rehan.passthrough.Passthrough;
import java.util.UUID;
import net.minecraft.client.player.AbstractClientPlayer;
import net.minecraft.client.resources.DefaultPlayerSkin;
import net.minecraft.world.entity.player.PlayerSkin;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** With -Dpassthrough.steveSkin=true the player is classic Steve while a host is attached; otherwise the account's own skin. */
@Mixin(AbstractClientPlayer.class)
abstract class AbstractClientPlayerMixin {
	/** DefaultPlayerSkin picks floorMod(uuid.hashCode(), 18); index 15 is entity/player/wide/steve. */
	private static final UUID CLASSIC_STEVE = new UUID(0L, 15L);

	@Inject(method = "getSkin", at = @At("HEAD"), cancellable = true)
	private void passthrough$steve(final CallbackInfoReturnable<PlayerSkin> cir) {
		if (Passthrough.active && Boolean.getBoolean("passthrough.steveSkin")) {
			cir.setReturnValue(DefaultPlayerSkin.get(CLASSIC_STEVE));
		}
	}
}
