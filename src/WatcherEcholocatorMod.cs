using BepInEx;
using HUD;
using RWCustom;
using System.Collections.Generic;
using System.Linq;
using System.Security.Permissions;
using UnityEngine;

#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace WatcherEcholocator
{
	[BepInPlugin("sabreml.watcherecholocator", "WatcherEcholocator", VERSION)]
	public class WatcherEcholocatorMod : BaseUnityPlugin
	{
		// The current mod version. (Stored here as a variable so that I don't have to update it in as many places.)
		public const string VERSION = "1.2.0";

		// Dict of regions where there's still an echo to find, and the number of echoes in the region.
		// (Includes empty regions)
		private Dictionary<string, int> remainingEncounterRegions;

		// Dic of region icons in the watcher warp map, and the attached glowy sprite added by this mod.
		private readonly Dictionary<Map.WarpRegionIcon, RegionIconGlowSprite> regionIconGlowSprites = [];

		// Bool indicating if the player has seen the first ending of the expansion.
		// If they have, then the glowy sprites around region icons isn't useful anymore, and should be skipped.
		private bool playerHasEndingOne;

		public void OnEnable()
		{
			On.HUD.Map.LoadWarpConnections += Map_LoadWarpConnectionsHK;
			On.HUD.Map.Draw += Map_DrawHK;

			On.HUD.Map.WarpRegionIcon.AddGraphics += WRI_AddGraphicsHK;
			On.HUD.Map.WarpRegionIcon.DestroyGraphics += WRI_DestroyGraphicsHK;
			On.HUD.Map.WarpRegionIcon.UpdateGraphics += WRI_UpdateGraphicsHK;
		}

		// Called when the map (regular map or warp map) is first loaded by the game.
		// (This is all done every time the map is loaded instead of being cached in case the player finds a new echo while playing.)
		private void Map_LoadWarpConnectionsHK(On.HUD.Map.orig_LoadWarpConnections orig, Map self)
		{
			// Only actually do anything if it's the watcher's warp map being loaded.
			if (self.mapData.type != Map.MapType.WarpLinks)
			{
				orig(self);
				return;
			}

			// Get the current savegame from `RainWorld.progression`.
			// If the player has starved themselves, the savestate is moved over to `starvedSaveState` and `currentSaveState` is made null.
			SaveState saveState = Custom.rainWorld.progression.currentSaveState ?? Custom.rainWorld.progression.starvedSaveState;

			// If the player has seen Watcher ending 1 (Ancient Urban), then any remaining echoes won't spawn anymore.
			// (See `Watcher.SpinningTop.Update()` and `Watcher.SpinningTop.Ascended` for more details.)
			if (saveState.deathPersistentSaveData.sawVoidBathSlideshow)
			{
				Debug.Log("(WatcherEcholocator) Player has seen Ending 1, and echoes will no longer spawn. Skipping region glow!");
				playerHasEndingOne = true;
				orig(self);
				return;
			}

			Debug.Log("(WatcherEcholocator) Loading encounter list...");

			// Copy over the `regionSpinningTopRooms`' regions into a new dictionary, with each region's value set to the number of encounters in them.
			remainingEncounterRegions = Custom.rainWorld.regionSpinningTopRooms.ToDictionary(pair => pair.Key, pair => pair.Value.Count);
			// `regionSpinningTopRooms` is a `Dictionary<string, List<string>>` of all regions codes in the game (keys),
			// and lists containing the room and `spawnIdentifier` of the echo encounter(s) in that region (value). (or an empty list if there aren't any)

			// This is just here so that it can be printed to the debug log.
			List<string> foundEncounters = [];

			foreach (KeyValuePair<string, List<string>> pair in Custom.rainWorld.regionSpinningTopRooms)
			{
				// No encounters in this region.
				if (pair.Value.Count == 0)
				{
					continue;
				}

				foreach (string encounter in pair.Value)
				{
					// The list's contents are formatted as `cc_c12:15` (`room:ID`). We just want the ID.
					int encounterID = int.Parse(encounter.Split(':')[1]);

					// See if the player has already had this encounter.
					if (saveState.deathPersistentSaveData.spinningTopEncounters.Contains(encounterID))
					{
						// If so, remove one entry from the `remainingEncounterRegions` dict.
						remainingEncounterRegions[pair.Key]--;
						foundEncounters.Add(encounter);
					}
				}
			}

			// Print out the list of regions with an echo in them, for debugging purposes.
			IEnumerable<string> remainingRegions = remainingEncounterRegions.Where(pair => pair.Value > 0).Select(pair => pair.Key);
			Debug.Log($"(WatcherEcholocator) Encounter list loaded! Remaining: ({string.Join(",", remainingRegions)})");

			orig(self);
		}

		// Update the glow sprite animation along with the rest of the map.
		private void Map_DrawHK(On.HUD.Map.orig_Draw orig, Map self, float timeStacker)
		{
			orig(self, timeStacker);
			foreach (RegionIconGlowSprite glowSprite in regionIconGlowSprites.Values)
			{
				glowSprite.Draw(timeStacker);
			}
		}


		// Called when a `WarpRegionIcon` is first created. If it's a region with an echo in it, add a glowy thing.
		private void WRI_AddGraphicsHK(On.HUD.Map.WarpRegionIcon.orig_AddGraphics orig, Map.WarpRegionIcon self)
		{
			orig(self);

			// Don't add any glow sprites if there's no echoes to find.
			if (playerHasEndingOne)
			{
				return;
			}

			// This shouldn't be possible, but just in case.
			if (remainingEncounterRegions == null)
			{
				string errorText = "(WatcherEcholocator) RemainingEncounterRegions is missing!";
				Debug.Log(errorText);
				Debug.LogException(new System.Exception(errorText));
				return;
			}
			// If this `WarpRegionIcon` doesn't have any remaining echo encounters.
			if (remainingEncounterRegions[self.region] == 0)
			{
				return;
			}

			// Add a glowy thing to the region icon.
			regionIconGlowSprites[self] = new RegionIconGlowSprite(self);
			//Debug.Log($"(WatcherEcholocator) Added glowy sprite to {Region.GetRegionFullName(self.region, null)}.");
		}

		// When a `WarpRegionIcon` is destroyed, also destroy its associated `RegionIconGlowSprite`.
		private void WRI_DestroyGraphicsHK(On.HUD.Map.WarpRegionIcon.orig_DestroyGraphics orig, Map.WarpRegionIcon self)
		{
			if (regionIconGlowSprites.TryGetValue(self, out RegionIconGlowSprite glowSprite))
			{
				glowSprite.Destroy();
				regionIconGlowSprites.Remove(self);
				//Debug.Log($"(WatcherEcholocator) Cleared glowy sprite for {Region.GetRegionFullName(self.region, null)}.");
			}
			orig(self);
		}

		// Update the `WarpRegionIcon`'s associated glowsprite so that they move with each other.
		private void WRI_UpdateGraphicsHK(On.HUD.Map.WarpRegionIcon.orig_UpdateGraphics orig, Map.WarpRegionIcon self)
		{
			orig(self);
			if (regionIconGlowSprites.TryGetValue(self, out RegionIconGlowSprite glowsprite))
			{
				glowsprite.UpdateGraphics();
			}
		}
	}

	// Holder class for the golden glowing sprite that this mod adds.
	public class RegionIconGlowSprite
	{
		// The region icon this is associated with.
		private Map.WarpRegionIcon warpRegionIcon;

		// The actual sprite itself.
		private FSprite glowSprite;

		// The current position along the 'glowing' sine wave. (See `Draw()`)
		// (static so that they all glow at the same time)
		private static float glowSin;

		// The size of the glow sprite.
		private const int SPRITE_SCALE = 13;
		// The speed at which the sprite glows/pulses in `Draw()`. (lower = faster)
		private const int GLOW_RATE = 8;
		// both of these are completely arbitrary

		public RegionIconGlowSprite(Map.WarpRegionIcon warpRegionIcon)
		{
			this.warpRegionIcon = warpRegionIcon;
			glowSprite = new("Futile_White", true)
			{
				shader = Custom.rainWorld.Shaders["FlatLight"],
				color = RainWorld.SaturatedGold
			};
			warpRegionIcon.map.container.AddChild(glowSprite);
		}

		// Clean up all references.
		public void Destroy()
		{
			warpRegionIcon = null;
			glowSprite.RemoveFromContainer();
			glowSprite = null;
			glowSin = 0f;
		}

		// Updates the values of the glow sprite.
		public void UpdateGraphics()
		{
			// Make the glow sprite line up with its corresponding region icon.
			glowSprite.x = warpRegionIcon.regionIcon.x;
			glowSprite.y = warpRegionIcon.regionIcon.y;
			glowSprite.scale = warpRegionIcon.regionIcon.scale * SPRITE_SCALE;

			// Increase `glowSin` by 0.1 every tick to scroll through the sine wave in `Draw()`.
			glowSin += 0.1f;
		}

		// Animates the glow sprite 'pulsing' slightly, as well as other alpha operations.
		public void Draw(float timeStacker)
		{
			// If the warp map isn't currently visible.
			if (!warpRegionIcon.map.visible)
			{
				glowSprite.alpha = 0f;
				return;
			}

			// If this region icon is currently selected/highlighted.
			if (warpRegionIcon.active)
			{
				// Make the glow's alpha slowly increase and decrease in pulses. (Stolen from `Menu.infoLabelSin`)
				glowSprite.alpha = Mathf.Lerp(1f, 0.7f, 1f * Mathf.Sin((glowSin + timeStacker) / GLOW_RATE));
			}
			else
			{
				// If it isn't selected, fade the alpha down to a background level.
				glowSprite.alpha = 0.20f;
			}

			// If the warp map is currently fading in as a whole, or this specific region has only just been discovered.
			// (If it is a new region, then it slowly fades in after everything else, then pulses in and out.)
			// See the game's `Map.WarpRegionIcon.UpdateGraphics()` method for more details.
			if (warpRegionIcon.map.fade < 1 || (warpRegionIcon.newRegion && warpRegionIcon.map.hud.owner.GetOwnerType() != HUD.HUD.OwnerType.FastTravelScreen))
			{
				// Adjust the glow sprite's alpha to fade along with it.
				glowSprite.alpha *= warpRegionIcon.circle.fade;
			}
		}
	}
}
