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
		public const string VERSION = "1.0.0";

		// Dict of regions where there's still an echo to find, and the number of echoes in the region.
		private Dictionary<string, int> remainingEncounterRegions = [];

		private Dictionary<Map.WarpRegionIcon, RegionIconGlowSprite> regionIconGlowSprites = [];

		public void OnEnable()
		{
			On.HUD.Map.LoadWarpConnections += LoadWarpConnectionsHK;
			On.HUD.Map.ClearSprites += ClearSpritesHK;
			On.HUD.Map.Draw += DrawHK;

			On.HUD.Map.WarpRegionIcon.AddGraphics += AddGraphicsHK;
			On.HUD.Map.WarpRegionIcon.UpdateGraphics += UpdateGraphicsHK;
		}

		private void LoadWarpConnectionsHK(On.HUD.Map.orig_LoadWarpConnections orig, Map self)
		{
			// Same as in the original method. Only bother doing any of this if it's the watcher warp map being loaded.
			if (self.mapData.type != Map.MapType.WarpLinks)
			{
				return;
			}

			Debug.Log("(WatcherEcholocator) Loading encounter list...");
			// Copy over the `regionSpinningTopRooms`' regions into a new dictionary, with each region's value set to the number of encounters in them.
			remainingEncounterRegions = Custom.rainWorld.regionSpinningTopRooms.ToDictionary(pair => pair.Key, pair => pair.Value.Count);
			// `regionSpinningTopRooms` is a `Dictionary<string, List<string>>` of all regions codes in the game,
			// with the lists containing the room and `spawnIdentifier` of the echo encounter(s) in that region. (or an empty list if there aren't any)

			// This is just here so that it can be printed to the debug log.
			List<string> foundEncounters = [];
			foreach (KeyValuePair<string, List<string>> pair in Custom.rainWorld.regionSpinningTopRooms)
			{
				// No encounters in this region.
				if (pair.Value.Count == 0)
				{
					continue;
				}

				string region = pair.Key;

				foreach (string encounter in pair.Value)
				{
					// The list's contents are formatted as `cc_c12:15` (`room:ID`). We just want the ID.
					int encounterID = int.Parse(encounter.Split(':')[1]);

					// See if the player has already had this encounter.
					if (Custom.rainWorld.progression.currentSaveState.deathPersistentSaveData.spinningTopEncounters.Contains(encounterID))
					{
						// If so, remove the entry from the `remainingEncounterRegions` dict.
						remainingEncounterRegions[region]--;
						foundEncounters.Add(encounter);
					}
				}
			}

			Debug.Log($"(WatcherEcholocator) Encounter list loaded! ({string.Join(",", foundEncounters)})");

			// This should all be done before the game does anything.
			orig(self);
		}

		private void ClearSpritesHK(On.HUD.Map.orig_ClearSprites orig, Map self)
		{
			foreach (RegionIconGlowSprite glowSprite in regionIconGlowSprites.Values)
			{
				glowSprite.Destroy();
			}
			regionIconGlowSprites.Clear();
			Debug.Log("(WatcherEcholocator) Glow sprites cleared.");
			orig(self);
		}

		private void DrawHK(On.HUD.Map.orig_Draw orig, Map self, float timeStacker)
		{
			orig(self, timeStacker);
			// Same as in the original method. Only bother doing any of this if it's the watcher warp map being loaded.
			if (self.mapData.type != Map.MapType.WarpLinks)
			{
				return;
			}

			foreach (RegionIconGlowSprite glowSprite in regionIconGlowSprites.Values)
			{
				glowSprite.Draw(timeStacker);
			}
		}


		private void AddGraphicsHK(On.HUD.Map.WarpRegionIcon.orig_AddGraphics orig, Map.WarpRegionIcon self)
		{
			orig(self);

			// If this `WarpRegionIcon` doesn't have any remaining echo encounters.
			if (remainingEncounterRegions[self.region] == 0)
			{
				return;
			}

			// Add a glowy thing to the region icon.
			regionIconGlowSprites.Add(self, new RegionIconGlowSprite(self));
			Debug.Log($"(WatcherEcholocator) Added glowy thing to {Region.GetRegionFullName(self.region, null)}.");
		}

		private void UpdateGraphicsHK(On.HUD.Map.WarpRegionIcon.orig_UpdateGraphics orig, Map.WarpRegionIcon self)
		{
			orig(self);
			if (regionIconGlowSprites.TryGetValue(self, out RegionIconGlowSprite glowsprite))
			{
				glowsprite.UpdateGraphics();
			}
		}


		#region Debugging Methods
		private void DebugPrintAllEncounterRegions(bool fullName = false)
		{
			Debug.Log("(WatcherEcholocator) All Encounter Regions:");
			Debug.Log(string.Join(", ", Custom.rainWorld.regionSpinningTopRooms
				.Where(pair => pair.Value.Count != 0)
				.Select(pair => (fullName ? Region.GetRegionFullName(pair.Key, null) : pair.Key) + $" ({pair.Value.Count})")
			));
		}
		private void DebugPrintAllRemainingEncounterRegions(bool fullName = false)
		{
			Debug.Log("(WatcherEcholocator) Remaining Encounter Regions:");
			Debug.Log(string.Join(", ", remainingEncounterRegions
				.Where(pair => pair.Value != 0)
				.Select(pair => (fullName ? Region.GetRegionFullName(pair.Key, null) : pair.Key) + $" ({pair.Value})")
			));
		}
		private void DebugPrintAllCompletedEncounterRegions(bool fullName = false)
		{
			Debug.Log("(WatcherEcholocator) Completed Encounter Regions:");
			Debug.Log(string.Join(", ", Custom.rainWorld.regionSpinningTopRooms
				.Where(pair => pair.Value.Count != 0 && remainingEncounterRegions[pair.Key] == 0)
				.Select(pair => fullName ? Region.GetRegionFullName(pair.Key, null) : pair.Key)
			));
		}
		#endregion
	}

	public class RegionIconGlowSprite
	{
		private Map.WarpRegionIcon warpRegionIcon;

		private FSprite glowSprite;

		// Current position along the 'glowing' sine wave. (See `Draw()`)
		private static float glowSin;

		// Size of the glow sprite.
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

		public void Destroy()
		{
			warpRegionIcon = null;
			glowSprite.RemoveFromContainer();
			glowSprite = null;
		}

		public void UpdateGraphics()
		{
			// Make the glow sprite line up with its corresponding region icon.
			glowSprite.x = warpRegionIcon.regionIcon.x;
			glowSprite.y = warpRegionIcon.regionIcon.y;
			glowSprite.scale = warpRegionIcon.regionIcon.scale * SPRITE_SCALE;

			// Increase `glowSin` by 0.1 every tick to scroll through the sine wave in `Draw()`.
			glowSin += 0.1f;
		}

		public void Draw(float timeStacker)
		{
			// If the map isn't currently visible.
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
				glowSprite.alpha = 0.20f;
			}

			// If the map is currently fading in, change the glow's alpha along with it.
			if (warpRegionIcon.map.fade < 1)
			{
				glowSprite.alpha *= warpRegionIcon.map.fade;
			}
		}
	}
}
