using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security;
using System.Security.Permissions;
using UnityEngine;
using RWCustom;
using BepInEx;
using Debug = UnityEngine.Debug;
using System.Runtime.CompilerServices;
using static SplitScreenCoop.SplitScreenCoop;
using BepInEx.Bootstrap;
using MonoMod.RuntimeDetour;
using UnityEngine.LowLevel;
using BepInEx.Logging;

#pragma warning disable CS0618

[module: UnverifiableCode]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace CoopLeash;

[BepInPlugin("WillowWisp.CoopLeash", "Stick Together", "1.0.0")]

/*
-Added a remix menu slider for Camera Margins and increased the default margins to give players more distance from the edge
-The "Spotlight" camera calling ability is now a setting in the remix menu, and it is now disabled by default
-A small player-colored progress bar will appear when holding jump to force-depart through a pipe
-Force-departing through a pipe no longer steals the camera if another defected player already has the camera
-Departing from a room won't cause the screen to split until the player's shortcut position actually transitions to the next room
-In splitscreen, with 2 players, players will keep the same camera number every time it splits
-Fixed groups departing early and leaving players behind if they were in a shortcut at the time
-Generally smoothed out the camera changes between rooms, and improved camera focus logic when "wait for everyone" is disabled
-Pressing random buttons while waiting in a pipe will display a quick reminder of the pipe controls

--WORKSHOP DESCRIPTION--

Help keep everyone on the same screen! Hold players in pipes until everyone is ready to go and teleport to other players with the Map button!
Quickly climb onto other player's backs by jumping and grabbing a standing player. Teleport tamed lizards and slugpups to you with the new Pet Leash feature

This mod works great for large co-op groups where lag issues (or skill issues) leave some players struggling to reach the exit pipe while the camera switches back and forth between rooms.

[h1]How It Works:[/h1]
[list]
[*]Entering a pipe will create a warp beacon for other players
[*]Tapping the MAP button will teleport you into the pipe with the beacon
[*]Tapping the MAP button again will exit the pipe
[*]Players cannot go through the beacon pipe until all players in the room enter the pipe
[*]Holding JUMP will depart without waiting for other players
[*](Only one beacon can exist at a time. Entering a non-beacon pipe while a beacon exists will send you through as normal)
[/list]

[h1]If SBCameraScroll mod is enabled[/h1]
[list]
[*]The camera will pan evenly between all players
[*]Tap the MAP button to toggle between group-focus and solo-focus
[*]Getting too far off-screen will remove you from group-focus until you get close enough to re-group
[/list]

Check the remix options menu to configure limits on teleportation or disabling certain features.
[hr]
Thanks to Nyuu for the thumbnail! and to Camiu for Chinese translations.
Check out my other mods! [url=https://steamcommunity.com/sharedfiles/filedetails/?id=2928004252] Rotund World [/url], Myriad (coming soon!)


----- TRANSLATABLE -----

Help keep your group close together by waiting inside pipes and teleporting to each other with the map button.
Climb on your friend's shoulders with jump and grab. 
If SBCameraScroll is enabled, the camera will focus on the center of the group


Players sitting idle in shortcut entrances will be pushed into the shortcut if another player is trying to enter that shortcut 
quick-piggyback can be done on players dangling from saint's tongue, grapple worms, or while treading surface water, regardless of if they are standing
Added safeguards to fix an issue where a room with a player offscreen would get unloaded and the player would be unable to take camera
Fixed an issue where disabling "allow splitting up" would make ALL shortcuts unusable while a warp beacon was active
*/

public partial class CoopLeash : BaseUnityPlugin
{
    private CLOptions Options;
    public static bool is_post_mod_init_initialized = false;
    public static bool shownHudHint = false;

    public CoopLeash()
    {
        try
        {
            Options = new CLOptions(this, Logger);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            throw;
        }
    }

    public static IntVector2 shortCutBeacon = new IntVector2(0, 0);
    public static Room beaconRoom;
    public static int groupFocusCount = 0;

    public static bool rotundWorldEnabled = false;
	public static bool camScrollEnabled = false;
	public static bool swallowAnythingEnabled = false;
    public static bool improvedInputEnabled = false;
    public static bool splitScreenEnabled = false;
    public static float internalCamZoom = 1f;
    public static float baseCameraZoom = 1f;
    public static bool zoomEnabled = true;
	public static float targetCamZoom = 1f;
	public static float lastCamZoom = 1f;
	public static bool cleanupDefectorFlag = false;

    private void OnEnable()
    {
        On.RainWorld.OnModsInit += RainWorldOnOnModsInit;
        On.RainWorld.PostModsInit += RainWorld_PostModsInit;
    }

    private void RainWorld_PostModsInit(On.RainWorld.orig_PostModsInit orig, RainWorld self)
    {
        orig(self);
        //I BARELY UNDERSTAND HOW THIS WORKS BUT SHUAMBUAM SEEMS TO HAVE IT ON LOCK SO I'LL JUST FOLLOW HIS LEAD
        if (is_post_mod_init_initialized) return;
            is_post_mod_init_initialized = true;
        
        if (camScrollEnabled)
        {
            CamScrollHooks();
        }

        if (improvedInputEnabled)
            Initialize_Custom_Input();
    }

    public static void CamScrollHooks()
    {
        //_ = new Hook(typeof(YeekFix.FixedYeekState).GetMethod(nameof(YeekFix.FixedYeekState.Feed)), FixedYeekState_Feed);
        _ = new Hook(typeof(SBCameraScroll.RoomCameraMod).GetProperty(nameof(SBCameraScroll.RoomCameraMod.Is_Camera_Zoom_Enabled)).GetGetMethod(), SB_CamZoom);

        baseCameraZoom = SBCameraScroll.MainModOptions.camera_zoom_slider.Value / 10f;
    }


    public static void Initialize_Custom_Input()
    {
        // wrap it in order to make it a soft dependency only;
        Debug.Log("Stick Together: Initialize custom input.");
        RWInputMod.Initialize_Custom_Keybindings();
        PlayerMod.OnEnable();
    }
	
	public static void SplitScreenHooks()
    {
         SplitScreenCoop.SplitScreenCoop.alwaysSplit = false;
         SplitScreenCoop.SplitScreenCoop.allowCameraSwapping = true;
        //THESE WOULD GET OVERWRITTEN...
        //IF NEEDED WE COULD JUST OVERWRITE THESE VALUES AS THEY ARE NEEDED, LIKE AT RainWorldGame_Update AND RoomCamera_ChangeCameraToPlayer
        //THIS DOESN'T SEEM TO WORK, FALLING BACK ON THE ABOVE OPTION
        /*
        SplitScreenCoop.SplitScreenCoop.Options.AlwaysSplit.Value = false;
        SplitScreenCoop.SplitScreenCoop.Options.AllowCameraSwapping.Value = true;

        if (SplitScreenCoop.SplitScreenCoop.Options.PreferredSplitMode.Value == SplitMode.Split4Screen)
            SplitScreenCoop.SplitScreenCoop.Options.PreferredSplitMode.Value = SplitMode.SplitVertical;
        */
    }
	

    private bool IsInit;
    private void RainWorldOnOnModsInit(On.RainWorld.orig_OnModsInit orig, RainWorld self)
    {
        orig(self);
        try
        {
            MachineConnector.SetRegisteredOI("WillowWisp.CoopLeash", Options);

            if (IsInit) return;

            for (int i = 0; i < ModManager.ActiveMods.Count; i++)
            {
                if (ModManager.ActiveMods[i].id == "willowwisp.bellyplus")
                    rotundWorldEnabled = true;
				if (ModManager.ActiveMods[i].id == "SBCameraScroll")
                    camScrollEnabled = true;
                if (ModManager.ActiveMods[i].id == "henpemaz_splitscreencoop")
                    splitScreenEnabled = true;
                if (ModManager.ActiveMods[i].id == "swalloweverything")
                    swallowAnythingEnabled = true;
                if (ModManager.ActiveMods[i].id == "improved-input-config")
                    improvedInputEnabled = true;
            }

            //Your hooks go here
            On.RainWorldGame.ShutDownProcess += RainWorldGameOnShutDownProcess;
            On.GameSession.ctor += GameSessionOnctor;

            On.RainWorldGame.Update += RainWorldGame_Update;
            On.RainWorldGame.ctor += RainWorldGame_ctor;
            On.ProcessManager.PostSwitchMainProcess += ProcessManager_PostSwitchMainProcess;

            On.ShortcutHandler.ShortCutVessel.ctor += ShortCutVessel_ctor;
            On.ShortcutHandler.SuckInCreature += ShortcutHandler_SuckInCreature;
            On.Creature.SuckedIntoShortCut += Creature_SuckedIntoShortCut;
            On.Creature.SpitOutOfShortCut += Creature_SpitOutOfShortCut;

            On.Player.Update += Player_Update;
            On.Player.GrabUpdate += Player_GrabUpdate;
            On.Player.ctor += Player_ctor;
            On.Player.JollyInputUpdate += Player_JollyInputUpdate;
            On.Player.checkInput += Player_checkInput;
            On.Player.TriggerCameraSwitch += Player_TriggerCameraSwitch;
            On.Player.TerrainImpact += Player_TerrainImpact;
            On.Player.Collide += Player_Collide;
            On.Player.Die += Player_Die;
            On.RoomCamera.Update += RoomCamera_Update;
            On.ShortcutHandler.Update += ShortcutHandler_Update;
            On.Menu.ControlMap.ctor += ControlMap_ctor;
            On.Lizard.Update += Lizard_Update;
            On.DeathFallGraphic.InitiateSprites += DeathFallGraphic_InitiateSprites;
            On.DeathFallGraphic.DrawSprites += DeathFallGraphic_DrawSprites;
            On.Player.PickupCandidate += Player_PickupCandidate;
            On.Watcher.WarpPoint.NewWorldLoaded_Room += WarpPoint_NewWorldLoaded_Room;

            On.JollyCoop.JollyHUD.JollyMeter.Draw += JollyMeter_Draw;
            On.JollyCoop.JollyHUD.JollyPlayerSpecificHud.JollyPlayerArrow.Draw += JollyPlayerArrow_Draw;
            On.JollyCoop.JollyHUD.JollyPlayerSpecificHud.JollyPlayerArrow.Update += JollyPlayerArrow_Update;
            On.JollyCoop.JollyHUD.JollyPlayerSpecificHud.JollyPlayerArrow.ClampScreenEdge += JollyPlayerArrow_ClampScreenEdge;

            //GRAPHICS FREEZES
            //On.RoomRain.DrawSprites += RoomRain_DrawSprites;
            On.MoreSlugcats.BlizzardGraphics.DrawSprites += BlizzardGraphics_DrawSprites;
            On.GlobalRain.Update += GlobalRain_Update;
            On.FlareBomb.Update += FlareBomb_Update;

            IsInit = true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            throw;
        }
    }

    private Vector2 JollyPlayerArrow_ClampScreenEdge(On.JollyCoop.JollyHUD.JollyPlayerSpecificHud.JollyPlayerArrow.orig_ClampScreenEdge orig, JollyCoop.JollyHUD.JollyPlayerSpecificHud.JollyPlayerArrow self, Vector2 input)
    {
        /*
        Vector2 origScreenSize = self.jollyHud.hud.rainWorld.options.ScreenSize;
        float camMod = GetCameraZoom(); //(1 / GetCameraZoom()
        self.jollyHud.hud.rainWorld.options.ScreenSize.Set(origScreenSize.x * camMod, origScreenSize.y * camMod);
        Vector2 result = orig(self, input);
        self.jollyHud.hud.rainWorld.options.ScreenSize.Set(origScreenSize.x, origScreenSize.y);
        return result;
        */

        
        if (camScrollEnabled && SmartCameraActive())
        {
            Vector2 screenSize = self.jollyHud.hud.rainWorld.options.ScreenSize;
            float camZoom = (1 / GetCameraZoom());
            float shiftMod = ((screenSize.x * camZoom) - screenSize.x) / 2f;
            input.x = Mathf.Clamp(input.x, self.screenEdge - shiftMod, (screenSize.x + shiftMod) - (float)self.screenEdge);
            float shiftModY = ((screenSize.y * camZoom) - screenSize.y) / 2f;
            input.y = Mathf.Clamp(input.y, self.screenEdge - shiftModY, (screenSize.y + shiftModY) - (float)self.screenEdge);
            return input;
        }
        else
            return orig(self, input);

    }

    private void RainWorldGame_ctor(On.RainWorldGame.orig_ctor orig, RainWorldGame self, ProcessManager manager)
    {
        orig(self, manager);
        //UNDO THOSE SETTINGS READINS
        if (SplitScreenActive())
            SplitScreenHooks();
    }

    public static bool SmartCameraActive()
    {
        return (CLOptions.smartCam.Value && camScrollEnabled);  //splitScreenEnabled
    }
	
	public static bool SplitScreenActive()
    {
        return (SmartCameraActive() && splitScreenEnabled);
    }

    public static bool CheckIfDualDisplays()
    {
        return SplitScreenCoop.SplitScreenCoop.dualDisplays && SplitScreenCoop.SplitScreenCoop.DualDisplaySupported(); //I GUESS THE FIRST ONE WOULD BE FALSE ANYWAYS BUT JUST TO BE SURE
    }

    private void GlobalRain_Update(On.GlobalRain.orig_Update orig, GlobalRain self)
    {
        orig(self);
        if (CLOptions.latencyMode.Value && Custom.rainWorld.processManager.IsGameInMultiplayerContext())
        {
            float dampener = 0f;
            self.ScreenShake *= dampener;
            self.MicroScreenShake *= dampener;
        }
    }

    int graphCounter = 40;
    private void BlizzardGraphics_DrawSprites(On.MoreSlugcats.BlizzardGraphics.orig_DrawSprites orig, MoreSlugcats.BlizzardGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        orig(self, sLeaser, rCam, timeStacker, camPos);
        //if (graphCounter <= 0)
        //    graphCounter = 40;
        if (CLOptions.latencyMode.Value && Custom.rainWorld.processManager.IsGameInMultiplayerContext())
        {
            //sLeaser.sprites[0].isVisible = false;
            sLeaser.sprites[1].isVisible = false;
        }
    }

    private void FlareBomb_Update(On.FlareBomb.orig_Update orig, FlareBomb self, bool eu)
    {
        orig(self, eu);

        if (CLOptions.latencyMode.Value && Custom.rainWorld.processManager.IsGameInMultiplayerContext())
        {
            if (self.burning > 0f)
            {
                float flashAvrg = 0.75f;
                float avgIntensity = self.LightIntensity;
                if (self.LightIntensity > 0.2f)
                    avgIntensity = 1.0f;

                self.lastFlickerDir = new Vector2(0, 0);
                self.flickerDir = new Vector2(0, 0);
                self.flashAplha = Mathf.Pow(flashAvrg, 0.3f) * avgIntensity;
                self.lastFlashAlpha = self.flashAplha;
                self.flashRad = Mathf.Pow(flashAvrg, 0.3f) * avgIntensity * 200f * 16f;
                self.lastFlashRad = self.flashRad;
            }

            if (self.light != null)
            {
                float flashAvrg = 0.75f;
                self.light.setAlpha = new float?(((self.mode == Weapon.Mode.Thrown) ? Mathf.Lerp(0.5f, 1f, flashAvrg) : 0.5f) * (1f - 0.6f * self.LightIntensity));
                self.light.setRad = new float?(Mathf.Max(self.flashRad, ((self.mode == Weapon.Mode.Thrown) ? Mathf.Lerp(60f, 290f, flashAvrg) : 60f) * 1f + self.LightIntensity * 10f));
            }
        }
    }


    //CORRECT SOME GRAPHICS THAT GET MESSED UP BY THE CAMERA ZOOM
    public static float deathPitGraphicSize = 0f;
    private void DeathFallGraphic_InitiateSprites(On.DeathFallGraphic.orig_InitiateSprites orig, DeathFallGraphic self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
    {
        orig(self, sLeaser, rCam);
        deathPitGraphicSize = sLeaser.sprites[0].scaleX;
    }

    private void DeathFallGraphic_DrawSprites(On.DeathFallGraphic.orig_DrawSprites orig, DeathFallGraphic self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        if (camScrollEnabled)
            sLeaser.sprites[0].scaleX = deathPitGraphicSize * (1 / GetCameraZoom());

        orig(self, sLeaser, rCam, timeStacker, camPos);
    }


    private PhysicalObject Player_PickupCandidate(On.Player.orig_PickupCandidate orig, Player self, float favorSpears)
    {
        PhysicalObject result = orig.Invoke(self, favorSpears);
        bool runExtra = false;
        if (result != null && result.grabbedBy.Count > 0 && result.grabbedBy[0].grabber is Player player && (self.onBack != null || player.onBack != null))
        {
            //Debug.Log("SOMEONE IS HOLDING THIS! LOOK FOR SOMETHING ELSE... ");
        }
        else
            return result;

        float closest = float.MaxValue;
        for (int i = 0; i < self.room.physicalObjects.Length; i++)
        {
            for (int j = 0; j < self.room.physicalObjects[i].Count; j++)
            {
                if (self.room.physicalObjects[i][j] is PlayerCarryableItem item && item.forbiddenToPlayer <= 0 && Custom.DistLess(self.bodyChunks[0].pos, item.bodyChunks[0].pos, item.bodyChunks[0].rad + 40f) && (Custom.DistLess(self.bodyChunks[0].pos, item.bodyChunks[0].pos, item.bodyChunks[0].rad + 20f) || self.room.VisualContact(self.bodyChunks[0].pos, item.bodyChunks[0].pos)) && self.CanIPickThisUp(item)
                    && !(item.grabbedBy.Count > 0 && item.grabbedBy[0].grabber is Player)) //IGNORE ITEMS BEING HELD BY ANOTHER PLAYER
                {
                    float dist = Vector2.Distance(self.bodyChunks[0].pos, item.bodyChunks[0].pos);
                    if (item is Spear)
                        dist -= favorSpears;
                    if (item.bodyChunks[0].pos.x < self.bodyChunks[0].pos.x == self.flipDirection < 0)
                        dist -= 10f;
                    if (dist < closest)
                    {
                        result = item;
                        closest = dist;
                    }
                }
            }
        }
        return result;
    }

    private void ControlMap_ctor(On.Menu.ControlMap.orig_ctor orig, Menu.ControlMap self, Menu.Menu menu, Menu.MenuObject owner, Vector2 pos, Options.ControlSetup.Preset preset, bool showPickupInstructions)
    {
        orig.Invoke(self, menu, owner, pos, preset, showPickupInstructions);

        if (self.pickupButtonInstructions != null)
        {
            self.pickupButtonInstructions.text += "\r\n" + menu.Translate("Stick Together Interactions:") + "\r\n";
            if (CLOptions.warpButton.Value)
                self.pickupButtonInstructions.text += "  - " + menu.Translate("Tapping the MAP button will teleport you into the pipe with the beacon") + "\r\n";
            
            self.pickupButtonInstructions.text += "  - " + menu.Translate("Tapping the MAP button again will exit the pipe") + "\r\n";

            if (CLOptions.allowForceDepart.Value)
                self.pickupButtonInstructions.text += "  - " + menu.Translate("Hold JUMP in a pipe to depart without waiting for other players") + "\r\n";

            if (CLOptions.quickPiggy.Value)
                self.pickupButtonInstructions.text += "  - " + menu.Translate("Jump and grab a standing player to piggyback onto them") + "\r\n"; 
        }
    }

    //SWOLLOW ANYTHING IS WEIRD SO. DO THIS INSTEAD
    public static bool SlugBackCheck(Player self)
	{
		return (ModManager.MSC || ModManager.CoopAvailable) && self.slugOnBack != null && !self.slugOnBack.interactionLocked && self.slugOnBack.slugcat == null; // && (self.spearOnBack == null || !self.spearOnBack.HasASpear)
	}

    public static bool CheckLizFriend(Lizard liz)
    {
        float likeness = 0; //liz.AI.LikeOfPlayer(dRelation.trackerRep);
        //if (ModManager.CoopAvailable) //JUST ASSUME THIS IS ON... IF NOT, YOU DESERVE IT - Custom.rainWorld.options.friendlyLizards)
        //{
		foreach (AbstractCreature checkCrit in liz.abstractCreature.world.game.NonPermaDeadPlayers)
		{
			Tracker.CreatureRepresentation player = liz.AI.tracker.RepresentationForCreature(checkCrit, false);
			likeness = Mathf.Max(liz.AI.LikeOfPlayer(player), likeness);
		}
        //}
        return likeness >= 0.5f;
    }

    public static void ScoopPups(Room myRoom, IntVector2 pipeTile)
	{
        //Debug.Log("SCOOPING PUPS");
		for (int i = 0; i < myRoom.abstractRoom.creatures.Count; i++)
		{
			if (myRoom.abstractRoom.creatures[i].realizedCreature is Player checkPlayer
                && checkPlayer.isNPC
				&& (ValidPlayerForRoom(checkPlayer, myRoom) || checkPlayer.inShortcut)
                && checkPlayer.onBack == null
                //&& checkPlayer.inShortcut == false
                && !(checkPlayer.dead || checkPlayer.dangerGraspTime > 0 || (checkPlayer.inShortcut && checkPlayer.grabbedBy.Count > 0)) //CHECK THESE NOW SINCE BEING IN A SHORTCUT NEGATES THE VALIDATION CHECK
				&& checkPlayer.AI != null && checkPlayer.AI.abstractAI.isTamed
            )
			{
                if (checkPlayer.inShortcut) //SPIT PUPS OUT OF SHORTCUTS SO THEY COME WITH US
                    checkPlayer.SpitOutOfShortCut(pipeTile, myRoom, true);
                checkPlayer.enteringShortCut = pipeTile; // shortCutBeacon;
			}
			
			//AND THEN DO LIZARDS TOO
			if (myRoom.abstractRoom.creatures[i].realizedCreature != null 
				&& myRoom.abstractRoom.creatures[i].realizedCreature is Lizard checkLizard
				&& checkLizard.room != null 
				&& checkLizard.room == myRoom
                && !checkLizard.dead
				&& checkLizard.AI.friendTracker.friend != null
                && checkLizard.AI.behavior != LizardAI.Behavior.ReturnPrey
                && CheckLizFriend(checkLizard)
            )
			{
                if (checkLizard.inShortcut)
                    checkLizard.SpitOutOfShortCut(pipeTile, myRoom, true);
                checkLizard.enteringShortCut = pipeTile;
			}
		}
	}
	

    private void JollyMeter_Draw(On.JollyCoop.JollyHUD.JollyMeter.orig_Draw orig, JollyCoop.JollyHUD.JollyMeter self, float timeStacker)
    {
        orig(self, timeStacker);
        if (!CLOptions.smartCam.Value)
            return;

        if (!spotlightMode)
        {
            self.cameraArrowSprite.alpha = 0;
        }
        else
        {
            self.customFade = 5f; //ALWAYS SHOW THE HUD IN SPOTLIGHT MODE (SO WE KNOW SOMEONE HAS CAM)
        }
    }

    private void JollyPlayerArrow_Draw(On.JollyCoop.JollyHUD.JollyPlayerSpecificHud.JollyPlayerArrow.orig_Draw orig, JollyCoop.JollyHUD.JollyPlayerSpecificHud.JollyPlayerArrow self, float timeStacker)
    {
        if (SmartCameraActive())
        {
            //ADJUST THE DRAW POSITION FOR THE CAMERA'S FOCUS CREATURE SO THEIR NAMEPLATE DOESN'T FLOAT IN THE MIDDLE OF THE SCREEN
            Vector2 origBodPos = self.bodyPos;
            Vector2 origLastBodPos = self.lastBodyPos;
            if (!spotlightMode && self.jollyHud.RealizedPlayer != null && self.jollyHud.RealizedPlayer.room != null && self.jollyHud.RealizedPlayer.abstractCreature == self.jollyHud.Camera.followAbstractCreature)
            {
                self.bodyPos = self.jollyHud.RealizedPlayer.GetCat().bodyPosMemory - self.jollyHud.Camera.pos; // new Vector2 (self.jollyHud.Camera.pos.x, 0);
                //self.bodyPos = Vector2.Lerp(self.jollyHud.Camera.pos, self.jollyHud.RealizedPlayer.GetCat().bodyPosMemory, Mathf.Lerp(GetCameraZoom(), 1f, 0.5f)) - self.jollyHud.Camera.pos;
                //OKAY IT'S ONE OF THESE ONES THAT WE ONLY WANT TO APPLY TO THE X AXIS...
                //float bodpX = Mathf.Lerp(self.jollyHud.Camera.pos.x, self.jollyHud.RealizedPlayer.GetCat().bodyPosMemory.x, Mathf.Lerp(GetCameraZoom(), 1f, 0.5f)) - self.jollyHud.Camera.pos.x;
                //float bodpY = Mathf.Lerp(self.jollyHud.Camera.pos.y, self.jollyHud.RealizedPlayer.GetCat().bodyPosMemory.y, GetCameraZoom()) - self.jollyHud.Camera.pos.y;
                //self.bodyPos = new Vector2(bodpX, bodpY);
                self.lastBodyPos = self.bodyPos;
            }
            orig(self, timeStacker);
            self.bodyPos = origBodPos; //PUT IT BACK
            self.lastBodyPos = origLastBodPos;

            //TRY TO CENTER THEM
            Vector2 centerScreen = new Vector2(self.jollyHud.hud.rainWorld.options.ScreenSize.x / 2f, self.jollyHud.hud.rainWorld.options.ScreenSize.y / 2f);
            Vector2 zoomAdjusted = Vector2.Lerp(centerScreen, self.mainSprite.GetPosition(), GetCameraZoom());
            self.mainSprite.SetPosition(zoomAdjusted);

            //OKAY WAIT HOW DO THESE EVEN GET DESYNCED? IS SOME OTHER MOD MESSING WITH THE SPRITE POSITION?? -OH WAIT THERES ANOTHER VERSION OF THE BODYPOS... (TARGETPOS) FORGET THAT THOUGH
            if (self.mainSprite != null && self.label != null)
            {
                Vector2 namePos = self.ClampScreenEdge(self.mainSprite.GetPosition() + new Vector2(0f, 20f));
                self.label.SetPosition(namePos);
            }
        }
        else
        {
            orig(self, timeStacker);
        }

        if (!CLOptions.waitForAll.Value)
            return;
        //HIDE STACKS OF PLAYER NAMES WAITING IN PIPES SO IT'S EASIER TO TELL WHEN SOMEONE IS NOT ALL THE WAY IN
        if (self.jollyHud.RealizedPlayer != null && self.jollyHud.RealizedPlayer.inShortcut)
        {
            self.label.alpha = 0;
        }
    }

    //TIMEOUT TIMER IN FRONT OF THE LABEL
    private void JollyPlayerArrow_Update(On.JollyCoop.JollyHUD.JollyPlayerSpecificHud.JollyPlayerArrow.orig_Update orig, JollyCoop.JollyHUD.JollyPlayerSpecificHud.JollyPlayerArrow self)
    {
        orig(self);

        Player myPlayer = self.jollyHud.RealizedPlayer;
        if (myPlayer != null && myPlayer.GetCat().noCam > 0)
        {
            int displayTime = Mathf.CeilToInt(myPlayer.GetCat().noCam / 40f);
            self.label.text = "(" + displayTime + ") " + self.playerName;
            
            //if (displayTime == 0)
            //    self.label.text = self.playerName;
            //this.size.x = 5 * this.playerName.Length;
        }
        else
            self.label.text = self.playerName;

        //FOR TESTING
        if (myPlayer != null && myPlayer.GetCat().defector)
            self.label.text = "(D)" + self.label.text;

        if (myPlayer != null && myPlayer.GetCat().deniedSplitCam)
            self.label.text = "!" + self.label.text;

        self.size.x = self.label.text.Length;
    }


    public static bool TwoPlayerSplitscreenMode()
    {
        return (splitScreenEnabled && ModManager.CoopAvailable && Custom.rainWorld.options.JollyPlayerCount == 2);
    }

    public static bool TwoPlusPlayerSplitscreenMode()
    {
        return (splitScreenEnabled && ModManager.CoopAvailable && Custom.rainWorld.options.JollyPlayerCount >= 2);
    }


    private void Player_TriggerCameraSwitch(On.Player.orig_TriggerCameraSwitch orig, Player self)
    {
        if (CLOptions.allowDeadCam.Value == false && self.dead)
            return;

        //FOR SIGMA. WHILE SPLITSCREEN IS ACTIVE, DON'T ALLOW CAMERA CALLING UNLESS THE SCREEN IS SPLIT AND YOU ARE A DEFECTOR NOT ON EITHER SCREEN
        if (TwoPlusPlayerSplitscreenMode() && !(self.GetCat().defector && self.room?.game?.cameras[1]?.followAbstractCreature?.realizedCreature != self))
        {
            orig(self);
            return;
        }
        //THIS DIDN'T SEEM TO FIX ANYTHING
        //if (TwoPlayerSplitscreenMode() && self.room?.game?.cameras[self.playerState.playerNumber]?.followAbstractCreature?.realizedCreature == self)
        //{
        //    orig(self);
        //    return;
        //}

        if (SmartCameraActive() == false || self.timeSinceSpawned < 10)
        {
            if (self.room?.game?.cameras[0]?.followAbstractCreature?.realizedCreature != self && self.GetCat().noCam > 40)
            {
                //DO NOTHING
            }
            else
            {
                if (CLOptions.camPenalty.Value > 0 && self.timeSinceSpawned > 10 && self.room?.game?.cameras[0]?.followAbstractCreature?.realizedCreature != self)
                    self.GetCat().noCam = Math.Max(CLOptions.camPenalty.Value, 3) * 40;

                orig(self);
            }
            return;
        }

        //IGNORE CASES WHERE WE WERE JUST TRYING TO USE THE WARP PIPES //(self.room != null && self.room == beaconRoom  && ValidPlayer(self)) ||
        if ( self.enteringShortCut != null || self.GetCat().skipCamTrigger > 0)
            return;
		
		bool cycleSwitch = false;

        //IF WE ARE NOT IN THE MAIN GROUP, ADD TIME
        if ((self.GetCat().defector || !SmartCameraActive()) && self.room?.game?.cameras[0]?.followAbstractCreature?.realizedCreature != self && self.GetCat().noCam > 40)
        {
            return;
        }

        //IF WE ARE A DEFECTOR CALLING THE CAMERA FROM SOMEONE ELSE, SPOTLIGHT US RIGHT AWAY.
        if (self.GetCat().defector || UnTubedSlugsInRoom(self.room) <= 1 || groupFocusCount == 1) //IF WE ARE THE ONLY PLAYER IN THE ROOM (BUT NOT THE ONLY ONE STILL ALIVE) ALLOW SWITCH
        {
            if (self.room == null || (self.room?.game?.cameras[0]?.followAbstractCreature?.realizedCreature == self && Custom.rainWorld.options.cameraCycling))
			{
				cycleSwitch = true;
				spotlightMode = false;
            }
			else
			{
                //FOR SPLITSCREEN, IF WE'RE THE FOCUS OF CAM2 WHEN WE CALL THE CAMERA, JUST COLLAPSE (or un-collapse) THE SECOND SCREEN.
                if (splitScreenEnabled && self.room.game.cameras.Count() > 1 && self.room?.game?.cameras[1]?.followAbstractCreature?.realizedCreature == self)
                {
                    // self.GetCat().deniedSplitCam = !self.GetCat().deniedSplitCam; //TOGGLE THIS SETTING - NO THIS ISN''T ENOUGH!
                    //TOGGLE THIS SETTING FOR EVERYONE IN THE ROOM
                    bool toggleTo = !self.GetCat().deniedSplitCam;
                    for (int i = 0; i < self.room.game.Players.Count; i++)
                    {
                        Player plr = self.room.game.Players[i].realizedCreature as Player;
                        if (plr != null && !plr.dead && plr.GetCat().defector && plr.room == self.room?.game?.cameras[1].room)
                        {
                            plr.GetCat().deniedSplitCam = toggleTo;
                        }
                    }
                }

                //IF WE'RE STEALING FROM THE MAIN GROUP, PUT A COOLDOWN ON US...
                if (CLOptions.camPenalty.Value > 0 && !spotlightMode && self.room != null && self.room.game.AlivePlayers.Count > 2)
					self.GetCat().noCam = Math.Max(CLOptions.camPenalty.Value, 3) * 40;
                if (groupFocusCount == 1) //IF THERE'S ONLY ONE PERSON TO HAND IT TO, DON'T PUT IT IN SPOTLIGHT MODE. HE DON'T NEED IT
                    spotlightMode = false;
                else 
                    spotlightMode = true;
			}
		}
		else //ELSE. WE SHOULD JUST ALWAYS JUST TOGGLE
			spotlightMode = !spotlightMode;

        if (CLOptions.allowSpotlights.Value == false)
            spotlightMode = false;

        //Debug.Log("CAM SWITCH: " + self.playerState.playerNumber + " - " + cycleSwitch + " - " + Custom.rainWorld.options.cameraCycling + " NOW SPOTLIGHTED? " + spotlightMode);
        bool origCycle = Custom.rainWorld.options.cameraCycling;
		if (!cycleSwitch)
			Custom.rainWorld.options.cameraCycling = false; //PRETEND CYCLING DOESN'T EXIST 
        orig(self);
        Custom.rainWorld.options.cameraCycling = origCycle;

        //if (camScrollEnabled && !spotlightMode) //&& MultiScreens()
        //    ResetZoom();
    }

    private void Player_ctor(On.Player.orig_ctor orig, Player self, AbstractCreature abstractCreature, World world)
    {
        orig(self, abstractCreature, world);
		if (self.room != null)
			self.GetCat().lastRoom = self.room.roomSettings.name;
    }

    private void ProcessManager_PostSwitchMainProcess(On.ProcessManager.orig_PostSwitchMainProcess orig, ProcessManager self, ProcessManager.ProcessID ID)
    {
        orig(self, ID);
        if (self.currentMainLoop != null && ID != ProcessManager.ProcessID.Game)
        {
            WipeBeacon("ProcessManager_PostSwitchMainProcess");
        }
    }

    public static void WipeBeacon(string debugOutput)
    {
        beaconRoom = null;
        shortCutBeacon = new IntVector2(0, 0);
    }

    private void RainWorldGame_Update(On.RainWorldGame.orig_Update orig, RainWorldGame self)
    {
        orig(self);

        if (self.processActive && !self.GamePaused && self.cameras[0].room != null)
        {
            self.devToolsLabel.text = self.devToolsLabel.text + " : [" + beaconRoom?.roomSettings.name + "] - " + shortCutBeacon;
            if (graphCounter > 0)
                graphCounter--;
        }
		
		if (cleanupDefectorFlag)
			CleanupDefectors(self);

        if (splitScreenEnabled && SmartCameraActive())
            SplitscreenUpdate(self);
    }

    public static Player GetFirstUndefectedPlayer(RainWorldGame self)
    {
        for (int i = 0; i < self.Players.Count; i++)
        {
            Player plr = self.Players[i].realizedCreature as Player;
            if (plr != null && !plr.dead && !plr.GetCat().defector)
            {
                return plr;
            }
        }
        return null;
    }

    public static Player GetFirstDefectedPlayer(RainWorldGame self)
    {
        for (int i = 0; i < self.Players.Count; i++)
        {
            Player plr = self.Players[i].realizedCreature as Player;
            if (plr != null && !plr.dead && plr.GetCat().defector) //WE CAN INCLUDE PLAYERS IN SHORTCUTS HERE
            {
                return plr;
            }
        }
        return null;
    }



    public const int tickMax = 20;
	int tickClock = tickMax;
    public static Vector2 lastCamPos = new Vector2(0, 0);
    float turple = 1f;
    float xDistanceMemory = 0f;
    float yDistanceMemory = 0f;

    //public static float screenLimitMult = 0.8f; //!!1f
    //public static float xScreenLimit = 1100 * screenLimitMult;
    //public static float yScreenLimit = 600 * screenLimitMult;
    public static float marginsMult = 1.10f; //!!1.17f; //AT WHAT POINT SHOULD WE DEFECT FROM THE GROUP
    

    public static Vector2 ScreenLimit()
    {
        return new Vector2(1100 * CLOptions.screenLimitMult.Value, 600 * CLOptions.screenLimitMult.Value);
    }

    public static float GetRejoinMargins(float input)
    {
        return Mathf.Lerp(input, input * marginsMult, 0.5f); //AT WHAT POINT SHOULD WE REJOIN (HALFWAY BETWEEN THE DEFECT MARGINS AND BASE SCREEN MARGINS (FOR ZOOMING)
    }

    //1250f
    //675f
    //float xLimit = 1300 * (1f / GetWidestCameraZoom()) * ScreenSizeMod().x; //GetWidestCameraZoom
    //float yLimit = 700

    bool spotlightMode = false;
    public void RoomCamera_Update(On.RoomCamera.orig_Update orig, RoomCamera self) 
    {
        //OKAY. SO I'VE GOT A CRAZY IDEA....
        AbstractCreature origFollorCrit = null;
        Vector2 origBodyPos = new Vector2(0, 0);
		Vector2 origBodyPos2 = new Vector2(0, 0);
        bool shiftBody = false;
        bool requestSwap = false;
        AbstractCreature swapFollower = null;
        groupFocusCount = 0;

        if (!(splitScreenEnabled && CheckIfDualDisplays())) //IF THE FUNKY SPLITSCREEN DUAL MONITOR MODE IS ENABLED, JUST DON'T TOUCH CAMERA STUFF
        { 
            if (SmartCameraActive() && ModManager.CoopAvailable && !self.voidSeaMode && self.followAbstractCreature != null && self.followAbstractCreature.realizedCreature != null && self.followAbstractCreature.realizedCreature is Player && self.room != null && self.room.abstractRoom == self.followAbstractCreature.Room)
            {
            
			    if (splitScreenEnabled)
			    {
                    //FOR SPLITSCREEN, ALL NON-MAIN CAMERAS SHOULD ONLY FOCUS ON DEFECTORS
                    if (self.cameraNumber > 0)
                    {
                        if (!(self.followAbstractCreature.realizedCreature as Player).GetCat().defector)
                        {
                            //CHECK HOW MANY DEFECTORS WE HAVE.
                            int strike = self.cameraNumber - 1;
                            for (int i = 0; i < self.room.game.Players.Count; i++)
                            {
                                Player plr = self.room.game.Players[i].realizedCreature as Player;
                                if (plr != null && !plr.dead && !plr.inShortcut && plr.GetCat().defector)
                                {
                                    if (strike > 0)
                                        strike--;
                                    else
                                    {
                                        //Debug.Log("CHANGING CAMERA TO PLAYER " + plr.ToString());
                                        self.ChangeCameraToPlayer(plr.abstractCreature);
                                        //SBCameraZoom(self, 0f, 0f); //SET OUR ZOOM TO 1
                                    }
                                }
                            }
                        } 
                        //WE ALWAYS UPDATE ZOOM (BECAUSE WE NEED TO INHERIT CAM[0]'S ZOOM LEVEL)
                        SBCameraZoom(self, 0f, 0f); //RUNNING THIS AS ANYTHING OTHER THAN MAIN CAM JUST SETS ZOOM LEVEL TO MAIN CAM'S LEVEL
                        orig(self); //OKAY JUST RUN ORIG AND BE DONE
                        return;
                    }

                    //MAKE SURE MAIN CAM IS NOT FOLLOWING A DEFECTOR (OR DEAD PLAYER), IF ABLE
                    if (!TwoPlayerSplitscreenMode() && self.cameraNumber == 0 && ((self.followAbstractCreature.realizedCreature as Player).GetCat().defector))// || (self.followAbstractCreature.realizedCreature as Player).dead))
                    {
                        //Debug.Log("CHECKING MAIN CAM");
                        bool foundTarget = false;
                        for (int i = 0; i < self.room.game.Players.Count; i++)
                        {
                            Player plr = self.room.game.Players[i].realizedCreature as Player;
                            if (plr != null && !plr.dead && !plr.inShortcut && !plr.GetCat().defector)
                            {
                                self.ChangeCameraToPlayer(plr.abstractCreature);
                                foundTarget = true;
                                //Debug.Log("FIXING MAIN CAM");
                            }
                        }
                        if (!foundTarget)
                        {
                            Debug.Log("OKAY EVERYONE HERE IS EITHER DEAD OR A DEFECTOR, SO YOU ARE THE NEW GROUP LEADER");
                            //BUT FIRST, DEFECT EVERYONE ELSE. WE CAN'T HAVE ALL NON-DEFECTORS IN SEPERATE ROOMS
                            for (int i = 0; i < self.room.game.Players.Count; i++) //ACTUALLY WE MIGHT NOT HAVE EVEN NEEDED THIS PART BUT IT MAKES SENSE SO IM LEAVING IT IN
                            {
                                Player plr = self.room.game.Players[i].realizedCreature as Player;
                                if (plr != null) //CHECK FOR NULL DUMMY
                                    plr.GetCat().defector = true;
                            }
                            (self.followAbstractCreature.realizedCreature as Player).GetCat().defector = false;
                        }
                    }
                
                    //DO NOT ALLOW SPOTLIGHT MODE IF MULTIPLE SCREENS ARE ACTIVE, THAT'S MEAN (UNLESS ITS FOR A DEFECTOR)
                    if (self.cameraNumber == 0 && !(self.followAbstractCreature.realizedCreature as Player).GetCat().defector && MultiScreens())
                        spotlightMode = false;
                }
			
			
			
			
			    origFollorCrit = self.followAbstractCreature;
                origBodyPos = self.followAbstractCreature.realizedCreature.mainBodyChunk.pos;
			    origBodyPos2 = self.followAbstractCreature.realizedCreature.bodyChunks[1].pos;
				
			    //IF WE'RE FOLLOWING A DEFECTOR, IT'S ALWAYS A SPOTLIGHT
			    if ((self.followAbstractCreature.realizedCreature as Player).GetCat().defector)
				    spotlightMode = true;

                //THIS CHUNK IS REJOINING THE NON-SPOTLIGHT RUNNING BECAUSE FORCED DEFECTOR SPOTLIGHTS SHOULD CHECK TO AUTO REJOING
                float maxLeft = spotlightMode ? float.MaxValue : origBodyPos.x;
                float maxRight = spotlightMode ? 0f : origBodyPos.x;
                float maxDown = spotlightMode ? float.MaxValue : origBodyPos.y;
                float maxUp = spotlightMode ? 0f : origBodyPos.y;
			    float totalX = 0f;
			    float totalY = 0f;
			    int totalCnt = 0;
                int unPiped = 0;
			    //CHECK EACH VECTOR TO SEE WHO IS AT THE FURTHEST CORNERS
			    for (int i = 0; i < self.room.game.Players.Count; i++)
			    {
				    Player plr = self.room.game.Players[i].realizedCreature as Player;
				    if (plr != null && !plr.dead && self.room.abstractRoom == self.room.game.Players[i].Room)
				    {
					    //IF WE'RE A DEFECTOR, CHECK TO SEE IF WE CAN REJOIN THE GROUP REAL QUICK
					    if (plr.GetCat().defector && plr.GetCat().justDefected <= 0 
                            && plr.room == GetFirstUndefectedPlayer(self.room.game)?.room //ALSO, WE CAN'T REJOIN IF WE AREN'T IN THE SAME ROOM, DUMBO //self.room.game.cameras[0].room
                            && plr.GetCat().pipeType != "other") //DON'T TRACK IN SHORTCUTS, SINCE IT SEEMS THE GAME WILL CHECK YOUR POSITION FROM THE PREVIOUS ROOM. SNEAKY LITTLE SHITE...
					    {
                            //if (Mathf.Abs(plr.mainBodyChunk.pos.x - lastCamPos.x) < 650f && Mathf.Abs(plr.mainBodyChunk.pos.y - lastCamPos.y) < 325)
                            //if (Mathf.Abs(plr.mainBodyChunk.pos.x - lastCamPos.x) < 650f * 2f * (1f / GetWidestCameraZoom()) && Mathf.Abs(plr.mainBodyChunk.pos.y - lastCamPos.y) < 325 * 2f * (1f / GetWidestCameraZoom())) //self.lastPos
                            //if (Mathf.Abs(plr.mainBodyChunk.pos.x - lastCamPos.x) < 650f * 2f * (1f / GetWidestCameraZoom()) * ScreenSizeMod().x && Mathf.Abs(plr.mainBodyChunk.pos.y - lastCamPos.y) < 325 * 2f * (1f / GetWidestCameraZoom()) * ScreenSizeMod().y)
                            //if (Mathf.Abs(plr.mainBodyChunk.pos.x - lastCamPos.x) < 1300 && Mathf.Abs(plr.mainBodyChunk.pos.y - lastCamPos.y) < 700) //I GIVE UP... THIS IS THE BEST I CAN DO
                            //OKAY IT'S NOT PERFECT, I THINK SOME OF THESE VALUES NEED TO BE MULTIPLIED BY THE GETWIDESTCAMERAZOOM MODIFIER AS WELL, BUT IT'S WAAAY BETTER THAN IT USED TO BE
                            if (Mathf.Abs(plr.mainBodyChunk.pos.x - lastCamPos.x) < (GetRejoinMargins(ScreenLimit().x * ScreenSizeMod().x) - (xDistanceMemory / 2f)) * (1f / GetWidestCameraZoom()) && Mathf.Abs(plr.mainBodyChunk.pos.y - lastCamPos.y) < (GetRejoinMargins(ScreenLimit().y * ScreenSizeMod().y) - (yDistanceMemory / 2f)) * (1f / GetWidestCameraZoom()))
                            {
                                plr.GetCat().defector = false;
                                Debug.Log("UNDEFECTING " + plr.playerState.playerNumber);
                                //IF WE HAD BEEN FORCED OUT OF OUR GROUP, WE CAN AUTO-REJOIN OUT OF SPOTLIGHT MODE
                                if (plr.GetCat().forcedDefect && spotlightMode)
                                {
                                    spotlightMode = false;
                                    plr.GetCat().forcedDefect = false;
                                    plr.GetCat().camProtection = 40;
                                }
                            
                                //IF THE OFF-CAMERA FROM SPLITSCREEN WAS FOCUSED ON US, UN-SPLIT THE CAMERAS. YES WE NEED TO DO THIS RIGHT NOW OTHERWISE IT WILL MESS UP THE DEFECTOR CALCULATIONS BELOW
                                if (SplitScreenActive() && self.room?.game?.cameras[1]?.followAbstractCreature?.realizedCreature == plr)
                                {
                                    SplitscreenUpdate(self.room?.game);
                                }
                            }
					    }

                        //SKIP THIS CHECK IF WE ARE TRANSITIONING BETWEEN ROOMS. OUR UPDATES ARE STILL FROM THE PREVIOUS ROOM
                        //NEW RULE! IN SPOTLIGHT MODE, THE SPOTLIGHTED PLAYER IS EXEMPT FROM THIS CALCULATION (SO THEIR MOVEMENT DOESN'T DEFECT OTHER PLAYERS WAITING IN THE MAIN GROUP
                        if (!plr.GetCat().defector && plr.GetCat().pipeType != "other" && plr.mainBodyChunk != null) 
					    {
						    Vector2 plrPos = plr.mainBodyChunk.pos;
						    if (plrPos.x < maxLeft)
							    maxLeft = plrPos.x;
						    if (plrPos.x > maxRight)
							    maxRight = plrPos.x;
						    if (plrPos.y < maxDown)
							    maxDown = Mathf.Max(plrPos.y, 0); //DON'T PAN INTO DEATH PITS
						    if (plrPos.y > maxUp)
							    maxUp = plrPos.y;
								
						    //KEEP TRACK OF THESE STATS. WE'LL AVERAGE OUT THE POSITIONS OF EVERYONE SHORTLY
                            //ACTUALLY, SPOTLIGHTED PLAYERS CAN RUN THE ABOVE STUFF SO THEY STILL GET DEFECTED BUT THEY WON'T CONTRIBUTE TOWARDS THE GROUP AVERAGE POSITION
                            if (!(spotlightMode && self.followAbstractCreature == plr.abstractCreature))
                            {
                                totalX += plrPos.x;
                                totalY += plrPos.y;
                                totalCnt++;
                            }
                            //Debug.Log("WHO THIS? " + plr.playerState.playerNumber + " - " + plrPos);
                        }

                        //if ((spotlightMode && self.followAbstractCreature == plr.abstractCreature))
                            //Debug.Log("THAT'S MEEE " + plr.GetCat().defector + " - " + plr.GetCat().pipeType + " - " + !(spotlightMode && self.followAbstractCreature == plr.abstractCreature));

                        if (plr.GetCat().pipeType != "normal") //!plr.inShortcut
                        {
                            unPiped++; //KEEP TRACK OF THIS. WE NEED TO KNOW IF IT'S 0
                        }
                    }
			    }
                groupFocusCount = totalCnt;
                //Debug.Log("CAM STATS " + " FOLLOW: " + self.followAbstractCreature.realizedCreature + " - ROOM: " + self.followAbstractCreature.Room.name);

                //FIND THE HIGHEST PLAYER ON SCREEN
                if (self.room.abstractRoom == self.followAbstractCreature.Room) //OKAY APPARENTLY THE GAME WILL TRY AND RUN THIS WHILE self.room.abstractRoom DOES NOT MATCH self.followAbstractCreature WHICH IS SUPER WEIRD
                {
                    for (int i = 0; i < self.room.game.Players.Count; i++)
                    {
                        Player plr = self.room.game.Players[i].realizedCreature as Player;
                        if (plr != null && !plr.dead && self.room.abstractRoom == self.room.game.Players[i].Room && !plr.inShortcut && !plr.GetCat().defector)
                        {
                            if (unPiped > 0 && (plr.mainBodyChunk.pos.y == maxUp) && maxUp > origBodyPos.y + 50)//self.followAbstractCreature.pos.y < maxUp - 50) //REMEMBER THIS CREATURE, WE MIGHT NEED TO SWAP TO THEM LATER
                            {
                                swapFollower ??= plr.abstractCreature; //OH IT CAN'T BE JUST ANY CREATURE. IT NEEDS TO BE THE HIGHEST CREATURE, I THINK...
                                //Debug.Log("SWAP " + plr.mainBodyChunk.pos.y + " FOLLOW: " + origBodyPos.y + " - ROOM: " + self.room.game.Players[i].Room.name);
                            }
                        }
                    }
                }


                //SKIP THE REST OF THIS CHECK IF EVERYONE IS IN A SHORTCUT. JUST RUN IT AS NORMAL
                if (unPiped != 0)  //!spotlightMode
                {

                    //IF WE'RE FOCUSED ON SOMEONE IN A PIPE, QUICKLY SWITCH IT TO THE FIRST AVAILABLE NON-PIPE PLAUER
                    if ((self.followAbstractCreature.realizedCreature as Player).inShortcut && !(TwoPlayerSplitscreenMode() && MultiScreens())) //EXCEPT IF THE SCREEN IS SPLIT IN 2P SPLITSCREEN MODE
                    {
                        for (int i = 0; i < self.room.game.Players.Count; i++)
                        {
                            Player plr = self.room.game.Players[i].realizedCreature as Player;
                            if (plr != null && !plr.dead && self.room.abstractRoom == self.room.game.Players[i].Room && !plr.inShortcut)
                                swapFollower ??= plr.abstractCreature; //LITERALLY JUST ANYONE. THE GAME CAN SORT IT OUT NEXT TICK IF THAT'S A PROBLEM
                        }
                    }

                    if (SmartCameraActive()) //camScrollEnabled
                    {
                        if (unPiped == 1 || spotlightMode) // || MultiScreens()) //WE SKIP SOME OF THE IMPORTANT MAXLEFT/RIGHT CALCULATIONS IN SPOTLIGHT MODE, OOPS
                            SBCameraZoom(self, 0f, 0f);
                        else
                            SBCameraZoom(self, Mathf.Abs(maxLeft - maxRight), Mathf.Abs(maxUp - maxDown));
                    }

                    //Debug.Log("RUNNING THE THING " + maxLeft + " - " + maxRight + " - " + maxUp + " - " + maxDown + " - ");

                    //CHECK FOR DEFECTORS, PLAYERS WHO HAVE STRAYED TOO FAR FROM THE GROUP AVERAGE IN EITHER THE X OR Y AXIS
                    float avgX = totalX / totalCnt;
                    float avgY = totalY / totalCnt;
                    Player mostBehindPlayer = null;
                    float mostBehind = 0;
                    float xLimit = (ScreenLimit().x * marginsMult) * (1f / GetWidestCameraZoom()) * ScreenSizeMod().x; //1300
                    float yLimit = (ScreenLimit().y * marginsMult) * (1f / GetWidestCameraZoom()) * ScreenSizeMod().y; //700
                    xDistanceMemory = Mathf.Abs(maxLeft - maxRight);
                    yDistanceMemory = Mathf.Abs(maxUp - maxDown);

                    //X LIMIT
                    if (Mathf.Abs(maxLeft - maxRight) > xLimit)
                    {
                        for (int i = 0; i < self.room.game.Players.Count; i++)
                        { //DO IT AGAIN....
                            Player plr = self.room.game.Players[i].realizedCreature as Player;
                            if (plr != null && plr.GetCat().camProtection <= 0 && !plr.GetCat().defector && self.room?.abstractRoom == self.room.game.Players[i].Room)
                            {
                                float myBehind = Mathf.Abs(plr.mainBodyChunk.pos.x - avgX);
                                if (plr.GetCat().pipeType == "other" || plr.dangerGraspTime > 30 || plr.dead)
                                    myBehind *= 1.35f; //PLAYERS THAT ARE GRABBED OR IN A SHORTCUT GET HIGHER PRIORITY FOR BEING CHOSEN AS DEFECTOR
                                if (myBehind > mostBehind)
                                {
                                    mostBehindPlayer = plr;
                                    mostBehind = myBehind;
                                }
                            }
                        }
                    }
                    //Y LIMIT
                    else if (Mathf.Abs(maxUp - maxDown) > yLimit)
                    {
                        for (int i = 0; i < self.room.game.Players.Count; i++)
                        { //DO IT AGAIN....
                            Player plr = self.room.game.Players[i].realizedCreature as Player;
                            if (plr != null && plr.GetCat().camProtection <= 0 && !plr.GetCat().defector && self.room?.abstractRoom == self.room.game.Players[i].Room)
                            {
                                float myBehind = Mathf.Abs(plr.mainBodyChunk.pos.y - avgY);
                                if (plr.GetCat().pipeType == "other" || plr.dangerGraspTime > 30 || plr.dead)
                                    myBehind *= 1.35f; //PLAYERS THAT ARE GRABBED OR IN A SHORTCUT GET HIGHER PRIORITY FOR BEING CHOSEN AS DEFECTOR
                                if (myBehind > mostBehind)
                                {
                                    mostBehindPlayer = plr;
                                    mostBehind = myBehind;
                                }
                            }
                        }
                    }

                    //CROWN THE BIGGEST DEFFECTOR
                    if (mostBehindPlayer != null)
                    {
                        mostBehindPlayer.GetCat().defector = true;
                        mostBehindPlayer.GetCat().justDefected = 20;
                        if (!spotlightMode) //DON'T APPLY THIS IN SPOTLIGHT MODE OR IT WOULD APPLY TO PLAYERS WHO TAKE SPOTLIGHT AND WALK AWAY
                        {
                            mostBehindPlayer.GetCat().forcedDefect = true;
                            if (!CLOptions.autoSplitScreen.Value)
                                mostBehindPlayer.GetCat().deniedSplitCam = true; //AUTO DENY THE SPLIT CAM. THEY CAN CALL CAM IF THEY REALLY NEED IT
                        }
                        
                        if (mostBehindPlayer.abstractCreature == self.followAbstractCreature)
                        {
                            if (self.hud?.jollyMeter != null)
                                self.hud.jollyMeter.customFade = 10f; //SHOW THE HUD FOR A SECOND SO THEY KNOW THINGS CHANGED
                            mostBehindPlayer.GetCat().noCam = CLOptions.camPenalty.Value * 40; //GIVE THEM A CAM DELAY
                        }
                    }

                    //WTF IS SBCAMERASCROLL DOING?!?! WHY DOES THE FOCUS CREATURE NOT ALLOW CAMERA POS TO EVER GO ABOVE THEM!!! STUPIT.
                    //if (!spotlightMode && unPiped > 1 && self.followAbstractCreature.realizedCreature.mainBodyChunk.pos.y > maxDown) // && maxDown < maxUp - 50)
                    //    requestSwap = true;


                    //SHIFT THE PLAYER
                    float shift = 0.5f;
                    Vector2 adjPosition = new Vector2(Mathf.Lerp(maxLeft, maxRight, shift), Mathf.Lerp(maxDown, maxUp, shift));
                    lastCamPos = adjPosition;

                    if (!spotlightMode)
                    {
                        //OKAY NEW PLAN. TAKE THE DISTANCE WE ARE SUPPOSED TO TRAVEL, AND DOUBLE IT
                        (self.followAbstractCreature.realizedCreature as Player).GetCat().bodyPosMemory = Vector2.Lerp(self.followAbstractCreature.realizedCreature.bodyChunks[0].pos, self.followAbstractCreature.realizedCreature.bodyChunks[1].pos, 0.33333334f);
                        Vector2 slingShot = adjPosition - self.followAbstractCreature.realizedCreature.mainBodyChunk.pos;
                        self.followAbstractCreature.realizedCreature.mainBodyChunk.pos += new Vector2(slingShot.x * 2f, slingShot.y * (turple)); //WAIT IM SO CONFUSED... DOES ONLY THE X NEED TO BE DOUBLED???
                        (self.followAbstractCreature.realizedCreature as Player).GetCat().bodyPosMemory.y = self.followAbstractCreature.realizedCreature.mainBodyChunk.pos.y - (slingShot.y * 1f); //ADJUST THIS TOO
                        shiftBody = true;
                    }
			    }
            }
        }
        orig(self);
        
        if (tickClock > 0)
            tickClock--;

        //SHIFT THE BODY BACK IN PLACE (THE ORIGINAL ONE. IT COULD HAVE CHANGED DURING ORIG)
        if (shiftBody && origFollorCrit.realizedCreature != null)
        {
            origFollorCrit.realizedCreature.mainBodyChunk.pos = origBodyPos;
            origFollorCrit.realizedCreature.bodyChunks[1].pos = origBodyPos2;
        }

        //THANKS TO ALL THAT DUMB JANK FROM SBCAMERASCROLL, WE HAVE TO SWAP OUT OUR FOCUS CREATURE IF IT'S HIGHER THAN ANOTHER PLAYER ON SCREEN. YOU NITWIT
        if (swapFollower != null && !spotlightMode) //(requestSwap) // && (self.followAbstractCreature.realizedCreature as Player).timeSinceSpawned > 40)
        {
            //Debug.Log("COOPLEASH SWAPFOLLOWER DETECTED! " + swapFollower.realizedCreature);
            //self.followAbstractCreature = swapFollower; //IT'S SAFE TO JUST CHANGE THIS WITHOUT CALLING ChangeCameraToPlayer() AS LONG AS THEY ARE IN THE SAME ROOM
            self.ChangeCameraToPlayer(swapFollower); //BUT THEY MIGHT NOT BE IN THE SAME ROOM. NITWIT
            //ABSOLUTELY STUPIT
        }
        

        //SHOW THE BEACON FX AROUND THE PIPE
        Creature creature = (self.followAbstractCreature != null) ? self.followAbstractCreature.realizedCreature : null;
        if (creature != null && creature is Player slugger && beaconRoom != null && self.room == beaconRoom) {
            if (tickClock <= 0)
            {
                float lifetime = 25f;
                IntVector2 pos = shortCutBeacon;
                bool warpAvailable = !CLOptions.warpButton.Value || (CheckBodyCountPreq(self.room) && CheckProxPreq(self.room));
                float strainMag = warpAvailable ? 40f : 15f;
                float mult = warpAvailable ? 1f : 1f;
                self.room.AddObject(new ExplosionSpikes(self.room, pos.ToVector2() * 20f, 15, 15f, lifetime, 5.0f, strainMag, new Color(1f, 1f, 1f, 0.9f * mult)));
                tickClock = tickMax;
            }
            ShowDepartProgressBars(self);
        }

        //SAFETY CHECK FOR ANY PLAYERS THAT MAY HAVE BEEN IN A SHORTCUT AS THE GAME PANIC INACTIVATES THE ROOM THEY ARE TRAVELING TO
        if (ModManager.CoopAvailable && self.room != null)
        {
            for (int i = 0; i < self.room.game.Players.Count; i++)
            {
                //THE REALIZED PLAYER IS NULL IIN THIS CASE! BUT THEIR ABSTRACT ROOM SHOULDN'T BE...
                AbstractCreature absPlayer = self.room.game.Players[i];
                if (absPlayer.Room != null && absPlayer.realizedCreature == null && !absPlayer.state.dead && RWInput.CheckSpecificButton(i, 11))
                {
                    //Debug.Log("RELOAD");
                    self.room.world.ActivateRoom(absPlayer.Room); //RE-LOAD THE ROOM! IN CASE IT WAS INACTIVE
                }
            }
        }
    }


    public static float zoomMemory = 1f;
    //public static float zoomUnlimited = 1f;
    public static void SBCameraZoom(RoomCamera self, float xExtra, float yExtra)
    {
        //if (MultiScreens())
        //    return; //NOT COMPATIBLE WITH THE ZOOM FEATURE ATM, UNFORTUNATELY...
        if (!zoomEnabled)
            return; //JUST FOR NOW....
        
        float xLimit = ScreenLimit().x * ScreenSizeMod().x; //DON'T PUT THE CAMERA MODIFIER ON THIS ONE
        float yLimit = ScreenLimit().y * ScreenSizeMod().y; //z
        float baseZoom = baseCameraZoom;  //WE GET THIS FROM SBCAM REMIX SLIDER, IF IT'S ENABLED ON STARTUP. OTHERWISE IT'S JUST 1
        float zoomMod = 1f + Mathf.Max((xExtra - xLimit) / xLimit, (yExtra - yLimit) / yLimit, 0f);
        float zoomLimit = 1f - CLOptions.zoomLimit.Value;
        zoomMod = Mathf.Max(baseZoom * (1f / zoomMod), zoomLimit);
		
		//DON'T ZOOM IN SINGLE SCREEN ROOMS, EL BOZO
		if (self.room.cameraPositions.Length <= 1)
			zoomMod = 1f;

        if (self.cameraNumber == 0)
            zoomMemory = zoomMod;
        else
            zoomMod = zoomMemory;
        //Debug.Log("ZOOM: " + zoomMod + " - " + xExtra + " - " + self.cameraNumber);

        if (camScrollEnabled && self.cameraNumber == 0)
            SBCameraApply(zoomMod);

        ApplyCameraZoom(self, GetCameraZoom()); //zoomMod
    }

    public static void SBCameraApply(float zoomMod)
    {
        if (SBCameraScroll.RoomCameraMod.camera_zoom != zoomMod)
        {
            // SBCameraScroll.RoomCameraMod.camera_zoom = zoomMod;
            
            lastCamZoom = SBCameraScroll.RoomCameraMod.camera_zoom;
            targetCamZoom = zoomMod;
            SBCameraScroll.RoomCameraMod.camera_zoom = Mathf.Lerp(lastCamZoom, targetCamZoom, 0.10f);
            //SBCameraScroll.RoomCameraMod.Apply_Camera_Zoom(self); //THIS DOESN'T RUN IF CAMERA ZOOM == 1
        }
    }


    //COPIED FROM SBCAMERA SCROLL, WHICH WAS COPIED FROM EYEBROW RAISE MOD. (OUR VERSION STILL RUNS WHEN CAMERA ZOOM == 1)
    public static void ApplyCameraZoom(RoomCamera room_camera, float camera_zoom)
    {
        for (int sprite_layer_index = 0; sprite_layer_index < 11; ++sprite_layer_index)
        {
            FContainer sprite_layer = room_camera.SpriteLayers[sprite_layer_index];
            sprite_layer.scale = 1f;
            sprite_layer.SetPosition(camOffsets[room_camera.cameraNumber]); //SetPosition(Vector2.zero);
            sprite_layer.ScaleAroundPointRelative(0.5f * room_camera.sSize, camera_zoom, camera_zoom);
        }
    }
	//THESE ARE FROM SPLITSCREEN, BUT SHOULD FIT IN WITH VANILLA
	public static Vector2[] camOffsets = new Vector2[] { new Vector2(0, 0), new Vector2(32000, 0), new Vector2(0, 32000), new Vector2(32000, 32000) }; // one can dream
	//WAIT!!!! THIS MIGHT BE USING THE WRONG MATH... THIS IS FOR 4 SPLITSCREENS. FOR 2 SPLITS IT HAS DIFFERENT VECTORS, I THINK?... OR MAYBE NOT..


    //public void SB_CamZoom(Func<SBCameraScroll.RoomCameraMod, null> orig, SBCameraScroll.RoomCameraMod self)
    public static bool SB_CamZoom(Func<bool> orig)
    {
        return SBCameraScroll.RoomCameraMod.camera_zoom != 1f;  //orig();
    }


    public static float GetCameraZoom()
    {
        if (camScrollEnabled)
            return GetSBCameraZoom();
        else if (splitScreenEnabled)
            return internalCamZoom;
        else
            return 1f;
    }

    public static float GetWidestCameraZoom()
    {
        if (camScrollEnabled && CLOptions.smartCam.Value)
            return 1f - CLOptions.zoomLimit.Value;
        else
            return 1f;
    }

    public static float GetSBCameraZoom()
    {
        if (SBCameraScroll.RoomCameraMod.Is_Camera_Zoom_Enabled)
            return SBCameraScroll.RoomCameraMod.camera_zoom;
        else
            return 1f;
    }

    //RESET OUR CAMERA ZOOM OUT TO MAX, TO CATCH PLAYERS IN RANGE OF THE GROUP CAMERA AS WE SWITCH AWAY FROM SPOTLIGHT MODE
    public static void ResetZoom()
    {
        if (MultiScreens() || !zoomEnabled)
            return; //NOT COMPATIBLE WITH THE ZOOM FEATURE ATM, UNFORTUNATELY...

        internalCamZoom = 1f - CLOptions.zoomLimit.Value;
        if (camScrollEnabled)
            SBResetZoom();
    }

    public static void SBResetZoom()
    {
        SBCameraScroll.RoomCameraMod.camera_zoom = 1f - CLOptions.zoomLimit.Value;
    }


    public static bool MultiScreens()
	{
        //return false;

        if (splitScreenEnabled)
			return GetScreenCount();
		else
			return false;
	}
	
	public static bool GetScreenCount()
	{
		return SplitScreenCoop.SplitScreenCoop.CurrentSplitMode != SplitMode.NoSplit;
	}
	
	
	public static Vector2 ScreenSizeMod()
	{
		if (splitScreenEnabled)
			return GetSplitScreenSize();
		else
			return new Vector2 (1f, 1f);
	}
	
	public static Vector2 GetSplitScreenSize()
    {
        float xScale = (SplitScreenCoop.SplitScreenCoop.CurrentSplitMode == SplitMode.SplitVertical || SplitScreenCoop.SplitScreenCoop.CurrentSplitMode == SplitMode.Split4Screen) ? 0.5f : 1f;
		float yScale = (SplitScreenCoop.SplitScreenCoop.CurrentSplitMode == SplitMode.SplitHorizontal || SplitScreenCoop.SplitScreenCoop.CurrentSplitMode == SplitMode.Split4Screen) ? 0.5f : 1f;

        return new Vector2(xScale, yScale);
    }

    public static bool SplitByRoom = false;

    public void SplitscreenUpdate(RainWorldGame self)
    {
        if (!self.IsStorySession) return;
        if (self.GamePaused) return;
         
        if (true) //self.cameras.Length > 1)
        {
            //IN 2 PLAYER SPLITSCREEN MODE, KEEP THE CAMERA NUMBERS CONSISTANT (UNLESS ONE IS DEAD, DUMMY!)
            if (TwoPlayerSplitscreenMode())
            {
                Player plr1 = self.Players[0].realizedCreature as Player;
                Player plr2 = self.Players[1].realizedCreature as Player;
                if (plr1 != null && plr2 != null && !plr1.dead && plr1.GetCat().defector)
                {
                    plr1.GetCat().defector = false;
                    plr2.GetCat().defector = true;
                }
            }

            bool splitGroup = false;
            bool unsheltered = false;
            bool shelteredUndefected = false;
            //CHECK HOW MANY DEFECTORS WE HAVE.
            for (int i = 0; i < self.Players.Count; i++)
            {
                Player plr = self.Players[i].realizedCreature as Player;
                //ALSO CHECK FOR DEFECTOR, SO BREAKAWAYS DON'T GET BLINDED AS THEY TRANSITION BETWEEN ROOMS. AND IN 2P SPLITSCREEN MODE IDK THIS JUST WORKS TOO
                if (plr != null && !plr.dead && !plr.GetCat().deniedSplitCam && (plr.GetCat().pipeType != "other" || plr.GetCat().defector || TwoPlayerSplitscreenMode())) 
                {
                    //DO WE CHECK BY DISTANCE OR BY ROOM?
					if (CLOptions.autoSplitScreen.Value || self.Players.Count <= 2) //IF THERE'S ONLY 2 OF US, NO REASON TO NOT USE DISTANCE
					{
                        if (plr.GetCat().defector)
                        {
                            splitGroup = true;
                            //break;
                        }
					}
                    //TRICK QUESTION, WE ALWAYS CHECK BY ROOM (AND SOMETIMES BY DISTANCE TOO)
                    if (plr.room != self.cameras[0].room && plr.room != null) //IS MY ROOM NULL WHILE IN A SHORTCUT???
                    {
                        splitGroup = true;
                        //break;
                    }
                    //SHELTER SWAP
                    if (plr.room != null)
                    {
                        if (plr.room.shelterDoor == null)
                            unsheltered = true; //AT LEAST ONE PERSON IS RUNNING AROUND OUTSIDE
                        else if (plr.room.shelterDoor != null && !plr.GetCat().defector && !plr.stillInStartShelter)
                            shelteredUndefected = true;
                    }  
                }
                //WE DELAY MOVING THIS 
                if (plr != null && plr.GetCat().stripMyDeniedSplitcam)
                {
                    plr.GetCat().deniedSplitCam = false;
                    plr.GetCat().stripMyDeniedSplitcam = false;
                }
                //VERY SPECIFIC CASE FOR 2-PLAYER SPLITSCREEN MODE. DON'T 
            }


            if (splitGroup && SplitScreenCoop.SplitScreenCoop.CurrentSplitMode == SplitMode.NoSplit)
            {
                //null.SplitScreenCoop.SplitScreenCoop.SetSplitMode(SplitScreenCoop.SplitScreenCoop.SplitMode.NoSplit, self);
                //(Chainloader.PluginInfos["com.henpemaz.splitscreencoop"].Instance as SplitScreenCoop.SplitScreenCoop).SetSplitMode(SplitScreenCoop.SplitScreenCoop.preferedSplitMode, self);
                //UnityEngine.Object.FindInstanceOfType(SplitScreenCoop.SplitScreenCoop)
                //SILLY OPTOMIZATION TIME... IF WE SHOULD SPLIT BUT BOTH OF OUR CAMERA TARGETS ARE ON THE SAME "SCREEN," DON'T SPLIT YET...
                if (!(self.cameras[0]?.room == self.cameras[1]?.room && self.cameras[0]?.currentCameraPosition == self.cameras[1]?.currentCameraPosition))
                    UnityEngine.Object.FindObjectOfType<SplitScreenCoop.SplitScreenCoop>().SetSplitMode(SplitScreenCoop.SplitScreenCoop.preferedSplitMode, self);
            }
            else if (!splitGroup && SplitScreenCoop.SplitScreenCoop.CurrentSplitMode != SplitMode.NoSplit)
            {
                //(Chainloader.PluginInfos["com.henpemaz.splitscreencoop"].Instance as SplitScreenCoop.SplitScreenCoop).SetSplitMode(SplitMode.NoSplit, self);
                UnityEngine.Object.FindObjectOfType<SplitScreenCoop.SplitScreenCoop>().SetSplitMode(SplitMode.NoSplit, self);
            }

            //OK WE NEED TO VERIFY THAT WHEN SOMEONE ELSE IS IN ANOTHER ROOM, THE TWO CAMERAS AREN'T FOCUSED ON UN-DEFECTED PEOPLE IN THE SAME ROOM
            if (splitGroup && self.cameras[1]?.followAbstractCreature?.realizedCreature is Player offcamPlr && !offcamPlr.GetCat().defector)
            {
                Player firstDefector = GetFirstDefectedPlayer(self.cameras[1].game);
                if (firstDefector != null)
                    self.cameras[1].ChangeCameraToPlayer(firstDefector.abstractCreature);
            }

            //SWAP DEFECTOR STATUS IF WE ARE IN A SHELTER WHILE SOMEONE ELSE ISN'T!!

            //IF AT LEAST ONE PERSON IS UNSHELTERED, MAKE THEM NON-DEFECTORS WHILE THE PLAYERS IN SHELTER ARE DEFECTORS
            if (unsheltered && shelteredUndefected)
            {
                Debug.Log("SWAP DEFECTOR STATUS FOR SHELTERED PLAYERS");
                for (int i = 0; i < self.Players.Count; i++)
                {
                    Player plr = self.Players[i].realizedCreature as Player;
                    if (plr != null && !plr.dead && plr.room != null)
                    {
                        if (plr.room.shelterDoor == null)
                            plr.GetCat().defector = false;
                        else
                            plr.GetCat().defector = true;
                    }
                }
            }

            //WHY DO WE NEED TO ENFORCE THIS EVERY TICK. WHY COULDNT YOU JUST WORK NORMAL
            if (TwoPlayerSplitscreenMode() && splitGroup) //IN 2 PLAYER SPLITSCREEN MODE, KEEP THE CAMERA NUMBERS CONSISTANT
            {
                Player plr1 = self.Players[0].realizedCreature as Player;
                Player plr2 = self.Players[1].realizedCreature as Player;
                if (!plr1.dead)
                {
                    if (self.cameras[0]?.followAbstractCreature != plr1?.abstractCreature)
                        self.cameras[0].ChangeCameraToPlayer(plr1.abstractCreature);
                    if (self.cameras[1]?.followAbstractCreature != plr2?.abstractCreature)
                        self.cameras[1].ChangeCameraToPlayer(plr2.abstractCreature);
                }
            }

            //UNDO THE ANNOYING ZOOM FEATURE SPLITSCREEN ADDED
            for (int i = 0; i < 2; i++)
            {
                if (SplitScreenCoop.SplitScreenCoop.cameraZoomed[i])
                {
                    //SplitScreenCoop.SplitScreenCoop.SetCameraZoom(cam, false); //HUH?? WHY NOT??
                    UnityEngine.Object.FindObjectOfType<SplitScreenCoop.SplitScreenCoop>().SetCameraZoom(self.cameras[i], false); //IS THIS REALLY HOW I GOTTA DO IT? GUESS SO...
                } 
            }
        }
    }

    public void UnSplitScreen(RainWorldGame self)
    {
        UnityEngine.Object.FindObjectOfType<SplitScreenCoop.SplitScreenCoop>().SetSplitMode(SplitMode.NoSplit, self);
    }



    public static bool ValidPlayer(Player player)
    {
        //MUST EXIST, NOT BE DEAD, AND NOT BE CAPTURED BY A CREATURE
        return (player != null && !(player.dead || player.playerState.permaDead) && player.dangerGraspTime < 1);
    }

    public static bool ValidPlayerForRoom(Player player, Room room) {

        return (player.room != null && player.room == room && ValidPlayer(player));
    }

    //OKAY LETS JUST HAVE THIS VERSION COUNT ANY TUBED SLUGS ANYWHERE
    public int TubedSlugs(Room room)
    {
        int pCount = 0;
        for (int i = 0; i < room.game.Players.Count; i++)
        {
            if (room.game.Players[i].realizedCreature != null
                && room.game.Players[i].realizedCreature is Player player
                //&& player.Consious
                && ValidPlayer(player)
                && ((player.inShortcut) && player.GetCat().pipeType != "normal")) //SHORT SHORTCUTS SHOULDN'T COUNT AS BEING TUBED (WEIRD I KNOW)
            {
                pCount += 1;
            }
        }
        return pCount;
    }

    //THIS ONES FOR ALL ROOMS
    public int UnTubedSlugs(Room room)
    {
        if (room == null)
            return 0;

        int pCount = 0;
        for (int i = 0; i < room.game.Players.Count; i++)
        {
            if (room.game.Players[i].realizedCreature != null
                && ValidPlayer(room.game.Players[i].realizedCreature as Player)
                && (room.game.Players[i].realizedCreature as Player).inShortcut == false)
            {
                pCount += 1;
            }
        }
        return pCount;
    }

    public int UnTubedSlugsInRoom(Room room) 
    {
        if (room == null)
            return 0;

        int pCount = 0;
        for (int i = 0; i < room.game.Players.Count; i++) {
            if (room.game.Players[i].realizedCreature != null 
				&& ValidPlayerForRoom(room.game.Players[i].realizedCreature as Player, room) 
                && (room.game.Players[i].realizedCreature as Player).inShortcut == false) 
				{
                pCount += 1;
            }
        }
        return pCount;
    }


    private void Player_JollyInputUpdate(On.Player.orig_JollyInputUpdate orig, Player self)
    {
        //If we're holding down the jump button past the first frame... skip orig >:3 evil
        if (self.onBack != null && self.input[0].jmp && self.input[1].jmp)
            return; //SO WE DON'T IMMEDIATELY LET GO IF WE ARE HOLDING JUMP WHEN WE GRABBED

        orig(self);
    }


    public Player GetSlugStackTop(Player slug)
    {
        while (slug != null && slug.slugOnBack != null && slug.slugOnBack.slugcat != null)
        {
            slug = slug.slugOnBack.slugcat;
            if (SlugBackCheck(slug)) //(slug.CanPutSlugToBack || (swallowAnythingEnabled && SlugBackCheck(slug)))
                break; //THIS IS OUR SLUG!
        }

        if (!SlugBackCheck(slug) && slug.Consious) //(slug.CanPutSlugToBack || (swallowAnythingEnabled && SlugBackCheck(slug)))
            return null;

        return slug;
    }




    private void Player_GrabUpdate(On.Player.orig_GrabUpdate orig, Player self, bool eu)
    {
        //SHOULD WE ATTEMPT TO CLIMB ON SOMEONES BACK?  //DON'T DO THIS IN ARENA
        if (CLOptions.quickPiggy.Value && self.input[0].pckp && !self.input[1].pckp && self.onBack == null && self.room != null && !self.isNPC && !self.pyroJumpped && !self.submerged && self.standing && self.lowerBodyFramesOffGround > 0 && (self.room?.game?.session is StoryGameSession))
        {
            //Debug.Log("ON WHO??" + self.onBack);
            float range = 26 + self.bodyChunks[1].rad;
            for (int i = 0; i < self.room.game.Players.Count; i++)
            {
                if (self.room.game.Players[i].realizedCreature != null
                    && self.room.game.Players[i].realizedCreature is Player player
                    && player != self
                    && player.room == self.room
                    && Custom.DistLess(self.bodyChunks[1].pos, player.bodyChunks[0].pos, range)
                    && player.Consious
                    && (self.slugOnBack == null || self.slugOnBack.slugcat != player) //DONT PIGGYBACK ONTO SOMEONE ON OUR BACK
                    && (player.standing || player.onBack != null || (player.tongue != null && player.tongue.Attached) || player.animation == Player.AnimationIndex.SurfaceSwim || player.animation == Player.AnimationIndex.GrapplingSwing) //Standing OR on someones back
                )
                {
                    Player newSeat = GetSlugStackTop(player);
                    if (newSeat != null && newSeat.slugOnBack != null) //newSeat.slugOnBack SHOULD NEVER BE NULL BUT ACCORDING TO EXCEPTION LOGS IT WILL...
                    {
                        //PUT US UP THERE!
                        newSeat.bodyChunks[0].pos += Custom.DirVec(self.firstChunk.pos, newSeat.bodyChunks[0].pos) * 2f;
                        newSeat.slugOnBack.SlugToBack(self);
                        self.dontGrabStuff = 20; //AND DON'T STEAL THEIR ITEM
                        break;
                    }
                }
            }
        }

        orig(self, eu);
    }


    //ROTUND WORLD SPECIFIC SHENANIGANS
    public void Player_checkInput(On.Player.orig_checkInput orig, Player self)
    {
        orig(self);
        //FOR ROTUND WORLD, SIMULATE DIRECTIONAL KEYPRESSES SO WE GET STUCK
        if (rotundWorldEnabled && self.enteringShortCut != null && self.enteringShortCut == shortCutBeacon)
        {
            self.input[0].x = -self.room.ShorcutEntranceHoleDirection(shortCutBeacon).x;
            self.input[0].y = -self.room.ShorcutEntranceHoleDirection(shortCutBeacon).y;
        }
    }

    public bool IsWarpPressed(Player player, bool piped)
    {
        if (player.isNPC)
            return false; //PUPS CAN'T PRESS BUTTONS
        if (!improvedInputEnabled)
        {
            if (piped)
                return (RWInput.CheckSpecificButton((player.State as PlayerState).playerNumber, 11));
            else
                return player.input[0].mp && !player.input[1].mp;
        }
        if (piped)
            return player.WantsToLeavePipe();
        else
            return player.WantsToWarp();
    }


    public bool CheckBodyCountPreq(Room room)
    {
        return !CLOptions.bodyCountReq.Value || TubedSlugs(room) >= UnTubedSlugsInRoom(room);
    }

    public bool CheckProxPreq(Room room)
    {
        if (!CLOptions.bodyCountReq.Value)
            return true;

        bool result = false;
        foreach (var absPlayer in room.world.game.AlivePlayers)
        {
            if (absPlayer.realizedCreature != null && absPlayer.realizedCreature is Player player && player.room == beaconRoom && ValidPlayer(player) && !player.inShortcut)
            {
                if (Custom.DistLess(player.bodyChunks[0].pos, player.room.MiddleOfTile(shortCutBeacon), CLOptions.proxDist.Value * 20))
                {
                    result = true;
                    break;
                } 
            }
        }
        return result;
    }


    private void Player_Update(On.Player.orig_Update orig, Player self, bool eu) 
    {
        orig(self, eu);

        //FOR TESTING
        //if (self.input[0].pckp && !self.input[1].pckp && self.input[0].thrw)
        //{
        //    turple += 0.1f;
        //    Debug.Log("TUBPLE " + turple);
        //}
        //if (self.input[0].pckp && self.input[0].thrw && !self.input[1].thrw)
        //    turple -= 0.1f;

        if (self.GetCat().skipCamTrigger > 0)
            self.GetCat().skipCamTrigger--;
		
		if (self.GetCat().forceDepart > 0)
            self.GetCat().forceDepart--;
		
		if (self.GetCat().noCam > 0)
			self.GetCat().noCam--;

        if (self.GetCat().camProtection > 0)
            self.GetCat().camProtection--;

        if (self.GetCat().justDefected > 0)
            self.GetCat().justDefected--;

        if (self.room != null && ValidPlayer(self)) {
            
            if (CLOptions.warpButton.Value && self.room == beaconRoom && !self.isNPC)
            {
                //CHECK FOR DISTANCE REQUIREMENTS TO TELEPORT! //CHECK IF ENOUGH SLUGS ARE IN THE TUBE TO ALLOW TELEPORT
                bool distReq = !CLOptions.proximityReq.Value || Custom.DistLess(self.bodyChunks[0].pos, self.room.MiddleOfTile(shortCutBeacon), CLOptions.proxDist.Value * 20);
                bool bodyReq = CheckBodyCountPreq(self.room);

                //TAP MAP TO TELEPORT!
                if (distReq && bodyReq && self.onBack == null) // && !(rotundWorldEnabled && self.room.abstractRoom.shelter)
                {
                    //TELEPORT TO THE BEACON PIPE!
                    if (IsWarpPressed(self, false) && self.shortcutDelay <= 0 && self.enteringShortCut == null)
                    {
                        self.enteringShortCut = shortCutBeacon;
                        self.GetCat().skipCamTrigger = 10;
                        self.PlayHUDSound(SoundID.MENU_Mouse_Grab_Slider);
                        if (self.tongue != null) //SAINT LET GO WITH YOUR TONGUE
                            self.tongue.Release();
                    }
                }
            }
			


            //TELEPORT TOWARDS THE DIRECITON OF THE SHORTCUT
            if (self.room != null && self.enteringShortCut != null && (self.enteringShortCut == shortCutBeacon || self.isNPC))
            { //MAKE SURE IT'S THE CORRECT SHORTCUT
                IntVector2 tpPos = (IntVector2)self.enteringShortCut; //shortCutBeacon
                Vector2 pos = (tpPos + self.room.ShorcutEntranceHoleDirection(tpPos)).ToVector2() * 20f;

                float xStretch = 1f;
                float yStretch = 1f;
                if (rotundWorldEnabled)
                {
                    if (self.room.ShorcutEntranceHoleDirection(tpPos).x != 0)
                    {
                        yStretch = self.room.abstractRoom.shelter ? 10 : 5f;
                        xStretch = self.room.abstractRoom.shelter ? 0f : 1f;
                    }
                    else
                    {
                        xStretch = self.room.abstractRoom.shelter ? 10 : 5f;
                        yStretch = self.room.abstractRoom.shelter ? 0f : 1f;
                    }
                }

                for (int i = 0; i < self.bodyChunks.Length; i++)
                {
                    self.bodyChunks[i].pos = new Vector2(Mathf.Lerp(self.bodyChunks[0].pos.x, pos.x + 10, 0.1f * xStretch), Mathf.Lerp(self.bodyChunks[i].pos.y, pos.y + 10, 0.1f * yStretch)); //+5 TO ACCOUNT FOR INACCURACY
                }
            }
        }
    }


    //LIZARDS NEED THE INCREASED WARP SPEED TOO
    private void Lizard_Update(On.Lizard.orig_Update orig, Lizard self, bool eu)
    {
        orig(self, eu);

        //TELEPORT TOWARDS THE DIRECITON OF THE SHORTCUT
        if (CLOptions.bringPups.Value && self.enteringShortCut != null && self.room != null && !self.dead)
        {
            IntVector2 tpPos = (IntVector2)self.enteringShortCut;
            Vector2 pos = (tpPos + self.room.ShorcutEntranceHoleDirection(tpPos)).ToVector2() * 20f;

            for (int i = 0; i < self.bodyChunks.Length; i++)
            {
                self.bodyChunks[i].pos = new Vector2(Mathf.Lerp(self.bodyChunks[0].pos.x, pos.x + 10, 0.1f), Mathf.Lerp(self.bodyChunks[i].pos.y, pos.y + 10, 0.1f)); //+5 TO ACCOUNT FOR INACCURACY
            }
        }
    }


    //CHECK TO MAKE SURE WE AREN'T ENTERING A PIPE FROM THE WRONG END SOMEHOW??
    private void Player_TerrainImpact(On.Player.orig_TerrainImpact orig, Player self, int chunk, IntVector2 direction, float speed, bool firstContact)
    {
        bool hasShortcut = (self.enteringShortCut != null);
        //HIGH SPEED LAUNCH INTO SHORTCUT DOESN'T CHECK FOR INPUT DIRECTION! THAT'S DUMB. FIX THAT SO WE DON'T RE-ENTER PIPE EXITS THAT WERE CLOGGED WITH PLAYERS
        orig(self, chunk, direction, speed, firstContact);
        //IF RUNNING THIS METHOD GAVE US A SHORTCUT, DOUBLE CHECK THINGS FOR US
        if ((self.enteringShortCut != null) && hasShortcut == false) 
        {
            IntVector2 intVector = self.room.ShorcutEntranceHoleDirection((IntVector2)self.enteringShortCut);
            if (!(self.input[0].x == -intVector.x && self.input[0].y == -intVector.y))
                self.enteringShortCut = null; //CANCL THAT SHORTCUT!
        }
    }

    private void Player_Collide(On.Player.orig_Collide orig, Player self, PhysicalObject otherObject, int myChunk, int otherChunk)
    {
        orig(self, otherObject, myChunk, otherChunk);
        //AUTOMATICALLY PUSH PLAYERS IDLING IN PIPE ENTRANCES INSIDE IF WE BUMP INTO THEM
        if (CLOptions.waitForAll.Value && otherObject is Player otherPlayer && otherPlayer.shortcutDelay <= 0 && self.shortcutDelay <= 0 && otherPlayer.touchedNoInputCounter > 20 && shortCutBeacon != new IntVector2(0, 0) && self.room != null)
        {
            //ONLY IF WE ARE CLOSE ENOUGH AND PUSHING IN THE DIRECTION OF THE PIPE
            IntVector2 intVector = self.room.ShorcutEntranceHoleDirection((IntVector2)shortCutBeacon);
            if (Custom.DistLess(otherPlayer.bodyChunks[0].pos, self.room.MiddleOfTile(shortCutBeacon), 18) && (self.input[0].x == -intVector.x && self.input[0].y == -intVector.y))
                otherPlayer.enteringShortCut = shortCutBeacon;
        }
    }

    //WHEN SOMEONE DIES, DOUBLE CHECK THAT THE OTHER PLAYERS AREN'T LOCKED AS DEFECTORS
    private void Player_Die(On.Player.orig_Die orig, Player self)
    {
        self.GetCat().deniedSplitCam = true;
        self.GetCat().defector = true;
        // RainWorldGame myGame = self?.room?.game;
        cleanupDefectorFlag = true;

        orig(self);
    }
	
	public static void CleanupDefectors(RainWorldGame myGame)
	{
        int aliveCount = 0;
        int defectCount = 0;
        int sameRoomCount = 1; //SINCE FIRST PLAYER MATCHES THEIR OWN ROOM, OFC
        string sharedRoom = "";
        for (int i = 0; i < myGame.Players.Count; i++)
        {
            Player plr = myGame.Players[i].realizedCreature as Player;
            if (plr != null && !plr.dead)
            {
                aliveCount++;
                if (plr.GetCat().defector)
                    defectCount++;
                //CHECK IF EVERYONE IS IN THE SAME ROOM !!AND!! IN THE SAME PIPE
                if (plr.GetCat().pipeType == "other")
                {
                    if (plr.GetCat().lastRoom == sharedRoom)
                        sameRoomCount++;
                    else
                    {
                        sharedRoom = plr.GetCat().lastRoom;
                    }
                }
                else //GOOFY...
                    sameRoomCount--;

                //Debug.Log("CLEANUP DEFECTORS: " + plr.playerState.playerNumber + " - " + plr.GetCat().lastRoom + " - " + plr.GetCat().pipeType);
            }
        }

        for (int i = 0; i < myGame.Players.Count; i++)
        {
            Player plr = myGame.Players[i].realizedCreature as Player;
            //Debug.Log("CLEANUP DEFECTORS: " + (defectCount == aliveCount) + " : " + (sameRoomCount == aliveCount) + " : " + (aliveCount == 1));
            if (plr != null)
            {
                if (!plr.dead)
                {
                    //UNDEFECT IF: EVERYONE IS DEFECTED | EVERYONE IS IN THE SAME EXIT PIPE | ONLY ONE LIVING PLAYER LEFT
                    if (defectCount == aliveCount || sameRoomCount == aliveCount || aliveCount == 1)
                    {
                        plr.GetCat().defector = false;
                        defectCount--; //IF EVERYONE WAS DEFECTED, WE ONLY NEED TO TURN ONE. THE REST WILL REJOIN VIA PROXIMITY
                    }
                }
                else
                { //THEY'RE DEAD. DEFECT THEM
                    plr.GetCat().defector = true;
                }
            }
        }

        cleanupDefectorFlag = false;
	}


    private void ShortcutHandler_Update(On.ShortcutHandler.orig_Update orig, ShortcutHandler self) {

        int othersInRoom = 0;
        int slugsInPipe = 0;
        for (int num = self.transportVessels.Count - 1; num >= 0; num--) 
        {
            if (ModManager.CoopAvailable && self.transportVessels[num]?.creature is Player player && !player.isNPC && self.transportVessels[num].room == beaconRoom?.abstractRoom) {
				bool forceDepartFlag = false;
                //IF WE PRESS THE MAP BUTTON, DUMP US OUT WHERE WE STAND! I THINK
                Room realizedRoom = self.transportVessels[num].room.realizedRoom; //DO WE REALLY NEED TO CHECK IF IT'S AN ENTRANCE?
                if (realizedRoom != null && realizedRoom.GetTile(self.transportVessels[num].pos).Terrain == Room.Tile.TerrainType.ShortcutEntrance)
				{
                    Player myPlayer = self.transportVessels[num].creature as Player;
                    if (myPlayer != null)
                    {
                        if (IsWarpPressed(myPlayer, true))// if (RWInput.CheckSpecificButton((myPlayer.State as PlayerState).playerNumber, 11, Custom.rainWorld))
                        {
                            self.SpitOutCreature(self.transportVessels[num]);
                            self.transportVessels.RemoveAt(num); //WILL THIS CAUSE ISSUES FOR THE LOOP?... -YES. THE ANSWER IS YES
                            myPlayer.GetCat().skipCamTrigger = 10;
                            myPlayer.PlayHUDSound(SoundID.MENU_Error_Ping);
                            //OKAY WE GOTTA DO SOMETHING NOW THAT WE'VE NUKED THIS ENTRY
                            //OK WERE WE THE ONLY ONE IN THIS BEACON SHORTCUT? END IT.
                            if (TubedSlugs(realizedRoom) == 0)
                            {
                                WipeBeacon("ShortcutHandler_Update");
                            }

                            return; //CAN WE DO THIS? DOES THIS WORK? THIS SEEMS LIKE A TERRIBLE IDEA
                        }

                        //IF WE'RE HOLDING DOWN THE JUMP BUTTON, LET US GO THROUGH PIPES ANYWAYS
                        if (CLOptions.allowForceDepart.Value && RWInput.CheckSpecificButton((myPlayer.State as PlayerState).playerNumber, 0))
                        {
                            myPlayer.GetCat().forceDepart++;
                            if (myPlayer.GetCat().forceDepart > 10)
                            {
                                self.transportVessels[num].wait = 0;
                                forceDepartFlag = true;
                                //FORCEFULLY SET THE CAMERA TO US BECAUSE WE PROBABLY WANT IT AND ALSO TO FIX UNLOADED ROOM ISSUES
                                if (SplitScreenActive() && !MultiScreens() && realizedRoom.game.cameras.Length > 1)
                                {
                                    if (TwoPlayerSplitscreenMode()) //NOT IN 2PSPLITSCREEN. OTHERWISE IT COULD TAKE THE WRONG CAM
                                        realizedRoom.game.cameras[myPlayer.playerState.playerNumber].followAbstractCreature = myPlayer.abstractCreature; //realizedRoom.game.cameras[myPlayer.playerState.playerNumber].ChangeCameraToPlayer(myPlayer.abstractCreature);
                                    else
                                        realizedRoom.game.cameras[1].ChangeCameraToPlayer(myPlayer.abstractCreature);
                                    myPlayer.GetCat().defector = true; //WHY WOULD WE NOT DEFECT??
                                }
                                else if (SmartCameraActive() && !splitScreenEnabled && !spotlightMode) //DON'T FOCUS US IF SPLITSCREEN IS ON. THE 2ND CAM WILL AUTO TARGET US
                                {
                                    realizedRoom.game.cameras[0].ChangeCameraToPlayer(myPlayer.abstractCreature);
                                    //myPlayer.GetCat().defector = true; //MAYBE WE DON'T NEED TO DO THIS?...
                                    spotlightMode = true;
                                }
                                //WIPE THE BEACON IF WE WERE THE ONLY ONE WAITING
                                if (TubedSlugs(realizedRoom) == 1)
                                    WipeBeacon("ShortcutHandler_Update");
                                myPlayer.PlayHUDSound(SoundID.MENU_Button_Standard_Button_Pressed);
                            }
                        }
                        else if (myPlayer.GetCat().forceDepart > 0)
                            myPlayer.GetCat().forceDepart = 0; //DECAY THE VALUE IF NOT HOLDING IT

                        //PEOPLE DON'T READ THE INSTRUCTIONS. IF THEY'RE PRESSING RANDOM BUTTONS WHILE WAITING IN A PIPE, REMIND THEM OF THE CONTROLS...
                        if (!shownHudHint && (RWInput.CheckSpecificButton((myPlayer.State as PlayerState).playerNumber, 3) || RWInput.CheckSpecificButton((myPlayer.State as PlayerState).playerNumber, 4)))
                        {
                            string msg = realizedRoom.game.rainWorld.inGameTranslator.Translate("Tapping the MAP button again will exit the pipe");
                            if (CLOptions.allowForceDepart.Value)
                                msg += ". Or " + realizedRoom.game.rainWorld.inGameTranslator.Translate("Hold JUMP in a pipe to depart without waiting for other players");
                            realizedRoom.game.cameras[0].hud.textPrompt.AddMessage(msg, 0, 200, false, false);
                            shownHudHint = true;
                        }
                    }
				}

                //CHECK FOR OTHER SLUGCATS IN THE ROOM
                Room room = beaconRoom;

                if (room != null)
                {
                    //CHECK FOR SLUGCATS IN THE ROOM FOR REAL THIS TIME
                    for (int i = 0; i < room.game.Players.Count; i++)
                    {
                        if (room.game.Players[i].realizedCreature != null && room.game.Players[i].realizedCreature is Player sluggo && ValidPlayer(sluggo) && sluggo != player)
                        {
                            if ((!sluggo.inShortcut || sluggo.GetCat().pipeType == "normal") && sluggo.GetCat().lastRoom == beaconRoom.roomSettings.name)
                            {
                                othersInRoom++;
                            }
                        }
                    }

                    //AS LONG AS THERE IS A PLAYER LEFT IN THE ROOM, DON'T DEPART
                    if (CLOptions.waitForAll.Value && othersInRoom > 0 && !forceDepartFlag)
                    {

                        if (shortCutBeacon != new IntVector2(0, 0) && room.MiddleOfTile(self.transportVessels[num].pos) == room.MiddleOfTile(shortCutBeacon))
                            self.transportVessels[num].wait = 2;
                        else
                            self.transportVessels[num].wait = 0; //THIS ISN'T THE BEACON, JUST GO RIGHT THROUGH!
                    }
                }
            }
        }

        orig(self);

        //WE MAY STILL HAVE TIME TO DO
        for (int num = self.transportVessels.Count - 1; num >= 0; num--)
        {
            if (self.transportVessels[num]?.creature is Player player2 && player2.GetCat().leavingStation > 0)
            {
                self.transportVessels[num].wait = player2.GetCat().leavingStation;
                player2.GetCat().leavingStation--;
            }
        }
    }


    public static Player PlayerHoldingMe(Player slug)
    {
        if (slug.grabbedBy?.Count > 0)
        {
            for (int graspIndex = slug.grabbedBy.Count - 1; graspIndex >= 0; graspIndex--)
            {
                if (slug.grabbedBy[graspIndex] is Creature.Grasp grasp && grasp.grabber is Player player_)
                    return player_; //THIS PLAYER IS HOLDING US
            }
        }
        return null; //NOBODY IS HOLDING US
    }



    //THIS VERSION DOES RUN FOR ALL CREATURES GETTING SUCKED IN
    public void Creature_SuckedIntoShortCut(On.Creature.orig_SuckedIntoShortCut orig, Creature self, IntVector2 entrancePos, bool carriedByOther)
    {
        if (self is Player player && !player.isNPC && player.room != null)
        {
            player.GetCat().lastRoom = player.room.roomSettings.name; //WILL THIS BE NON-NULL?
			if (self.room?.shortcutData(entrancePos).shortCutType == ShortcutData.Type.Normal) //AS OPPOSED TO ShortcutData.Type.RoomExit
                player.GetCat().pipeType = "normal";
            else
                player.GetCat().pipeType = "other";


            //OKAY REAL QUICK, IF THERE IS NO ONE IN THE SAME ROOM AS THE BEACON, DELETE IT.
            if (self.room != null && beaconRoom != null)
            {
                bool beaconConfirm = false;
                for (int i = 0; i < self.room.game.Players.Count; i++)
                {
                    if (self.room.game.Players[i].realizedCreature != null && self.room.game.Players[i].realizedCreature is Player sluggo && ValidPlayer(sluggo))
                    {
                        if (sluggo.GetCat().lastRoom == beaconRoom.roomSettings.name)
                            beaconConfirm = true;
                    }
                }
                if (!beaconConfirm)
                {
                    Debug.Log("BEACON ROOM EMPTY! WIPE BEACON ");
                    WipeBeacon("Creature_SuckedIntoShortCut");
                }
            }

            //DOUBLE CHECK IF WE'RE EVEN ALLOWED TO ENTER THIS PIPE!
            if (beaconRoom == self.room && shortCutBeacon != entrancePos && player.GetCat().pipeType != "normal")
            {
                if (CLOptions.allowSplitUp.Value == false)
                {
                    self.shortcutDelay = 20;
                    self.enteringShortCut = null;
                    (self as Player).PlayHUDSound(SoundID.MENU_Error_Ping);
                    return;//JUST CUT IT OUT! DON'T EVEN ENTER THE PIPE
                }
                //THIS ISN'T THE WARP BEACON PIPE, SO IT SHOULD DEFECT US!
                player.GetCat().defector = true;
                player.GetCat().departedFromAltExit = true;
            }

            //IF WE HAVE "WAIT FOR EVERYONE" DISABLED (FOR SOME GOD UNKOWN REASON) WE NEED TO DEFECT THE MOMENT WE ENTER A PIPE
            if (CLOptions.waitForAll.Value == false)
            {
                //if (!player.GetCat().defector) //DON'T DENY THE SPLITCAM IF WE ARE ALREADY A DEFECTOR. WE WANT OUR SPLITCAM TO STAY ACTIVE IN THE PIPE
                //    player.GetCat().deniedSplitCam = true;
                //OKAY WE DON'T NEED THAT BIT ANYMORE. WE JUST WON'T SPLIT WHILE CAMERAS ON THE SAME "SCREEN"
                player.GetCat().defector = true;
                cleanupDefectorFlag = true; //FOR GOOD MEASURE -OKAY FORGET YOU YOU SUCK
                //WE NEED TO HAND CAMERA TO THE FIRST PERSON WHO ENTERED THE EXIT PIPE
                if (TubedSlugs(player.room) == 0)
                {
                    player.GetCat().firstInAPipe = true;
                    //Debug.Log("FIRST TUBED SLUG!");
                }
                if (UnTubedSlugs(player.room) == 1)
                {
                    //Debug.Log("LAST TUBED SLUG!");
                    for (int i = 0; i < player.room.game.Players.Count; i++)
                    {
                        Player plr = player.room.game.Players[i].realizedCreature as Player;
                        if (plr != null && plr.GetCat().firstInAPipe)
                            self.room.game.cameras[0].followAbstractCreature = plr.abstractCreature;
                    }
                }
            }
        }
        orig(self, entrancePos, carriedByOther);
        //AND THEN ShortcutHandler.SuckInCreature RUNS (THE ONE UNDER US)


    }

    private void Creature_SpitOutOfShortCut(On.Creature.orig_SpitOutOfShortCut orig, Creature self, IntVector2 pos, Room newRoom, bool spitOutAllSticks)
    {
        orig(self, pos, newRoom, spitOutAllSticks);
        
        if (self is Player player && !player.isNPC)
        {
            player.GetCat().pipeType = "untubed";
			player.GetCat().lastRoom = newRoom.roomSettings.name;
            player.GetCat().leavingStation = 0;
            player.GetCat().stripMyDeniedSplitcam = true;
            player.GetCat().departedFromAltExit = false;
            player.GetCat().firstInAPipe = false;
            //IF WE ARE IN THE JAWS OF A CREATURE, WE SHOULD BE A DEFECTOR
            if (player.dangerGrasp != null) //(splitScreenEnabled && beaconRoom != null && beaconRoom != newRoom) //WE HANDLE THIS ELSEWHERE NOW, HOPEFULLY
                player.GetCat().defector = true;

            cleanupDefectorFlag = true; //JUST MAKE SURE WE DIDN'T AUTO DEFECT FROM A ROOM WITH THE ONLY OTHER DEFECTOR, LEAVING ALL PLAYERS AS DEFECTORS
        }
    }

    //APPARENTLY THIS DOES NOT RUN FOR CREATURES GETTING CARRIED INTO A SHORTCUT BY ANOTHER CREATURE. KEEP THAT IN MIND
    //HERES THE ORDER: SUCKINCREATURE IS CALLED ON THE MAIN CREATURE. THEN, AFTER THAT IS DONE, IT SETS Creature.inShortcut = true FOR ALL CONNECTED OBJECTS
    private void ShortcutHandler_SuckInCreature(On.ShortcutHandler.orig_SuckInCreature orig, ShortcutHandler self, Creature creature, Room room, ShortcutData shortCut) 
    {
        bool validShortType = shortCut.shortCutType == ShortcutData.Type.RoomExit; // || shortCut.shortCutType == ShortcutData.Type.Normal;
        if (CLOptions.waitForAll.Value && creature is Player player && !player.isNPC && ModManager.CoopAvailable && Custom.rainWorld.options.JollyPlayerCount > 1 && validShortType) {

            //!!!! FOR THIS TO WORK! WE NEED TO TAKE INTO ACCOUNT ALL SLUGCATS THAT WERE ON OUR BACK AS WE ENTER THIS PIPE, SINCE THE GAME WON'T SEE THEM AS "IN A PIPE" YET
            //DO NOT ACTIVATE UNLESS THERE ARE OTHER SLUGCATS IN THE ROOM. AND DEACTIVATE IF WE'RE THE LAST ONE
            int othersInRoom = 0;
			int slugsInPipe = 1; //it's us
			//HERE THIS VERSION CHECKS ALL PLAYERS, NOT JUST PLAYERS IN THE ROOOM (SINCE I BELEIVE PLAYERS IN SHORTCUTS NO LONGER COUNT AS IN THE ROOM)
			for (int i = 0; i < room.game.Players.Count; i++)
            {
                //Player sluggor = room.game.Players[i].realizedCreature as Player;
                //Debug.Log("FLAG 1 " + ValidPlayer(sluggor) + " : " + (sluggor != creature) + " : " + (sluggor.GetCat().lastRoom == player.GetCat().lastRoom));
                if (room.game.Players[i].realizedCreature != null && room.game.Players[i].realizedCreature is Player sluggo && ValidPlayer(sluggo) && sluggo != creature && sluggo.GetCat().lastRoom == player.GetCat().lastRoom) //creature.room -CHECK LASTROOM SINCE IT CHANGED AS WE ENTERED THIS PIPE. ALSO NULL WHEN IN SHORTCUTS? THE NAME AT LEAST...
                {
					Player slug = sluggo;
					while (slug.onBack != null) //ARE WE ON SOMEONES BACK?
					{
						slug = slug.onBack;
					}
					while (PlayerHoldingMe(slug) != null) //CHECK IF WE'RE BEING HELD BY ANOTHER SLUG
					{
						slug = PlayerHoldingMe(slug); //THAT'S HIM OFFICER
					}
                    //Debug.Log("PIPE TIME " + sluggo.playerState.playerNumber + " : " + slug.GetCat().pipeType + " : " + sluggo.GetCat().lastRoom + " : " + (beaconRoom == null || sluggo.GetCat().lastRoom == beaconRoom.roomSettings.name) + " - " + (slug.inShortcut || slug == creature));
                    //NOW!... IS WE IN THE PIPE OR NOT
                    if ((slug.GetCat().pipeType == "other" || slug == creature) && (beaconRoom == null || sluggo.GetCat().lastRoom == beaconRoom.roomSettings.name)) //WE CHECK FOR == CREATURE BECAUSE THEY ARE THE ONES ENTERING THE PIPE (but arent inShortcut yet)
						slugsInPipe++;
					else
						othersInRoom++;
                }
            }

            
            //ENTERING THE PIPE WILL BREAK CAMERA PANNING BECAUSE WE CAN NO LONGER STRETCH OUR TORSO. HAND CAMERA CONTROL TO SOMEONE ELSE! SOMEONE NOT IN A PIPE
            if (SmartCameraActive() && creature.abstractCreature == room.game.cameras[0].followAbstractCreature && othersInRoom > 0)
            {
                for (int i = 0; i < room.game.Players.Count; i++)
                {
                    if (room.game.Players[i].realizedCreature != null && room.game.Players[i].realizedCreature is Player sluggo && ValidPlayer(sluggo) && sluggo != creature && !sluggo.inShortcut && sluggo.room == creature.room)
                    {
                        sluggo.TriggerCameraSwitch();
                        spotlightMode = false;
                        break;
                    }
                }
            }


            // float req = 0.5f; //SET THE PERCENTAGE OF SLUGS THAT NEED TO BE INSIDE BEFORE YOU CAN TELEPORT
            if (othersInRoom > 0 && ValidPlayer(creature as Player))
			{
                if (shortCutBeacon == new IntVector2(0, 0)) {
                    shortCutBeacon = shortCut.StartTile;
                    beaconRoom = room;
                }
            }
            else if (player.room == beaconRoom)// && shortCutBeacon == shortCut.StartTile) { /WAIT NO, WE WANT THE OTHERS IN THE BEACON PIPE TO NOT BE TRAPPED IF THE LAST DUMMY TAKES THE WRONG ONE
            {
                //WE WERE THE ONLY ONE LEFT IN THE BEACON ROOM! REMOVE EXISTING BEACON
                //DON'T LEAVE THE KIDS BEHIND
                if (CLOptions.bringPups.Value)
                    ScoopPups(beaconRoom, shortCut.StartTile);

                orig(self, creature, room, shortCut); //WAIT, WE HAVE TO RUN OURS FIRST I THINK.
				
                //BUT FIRST, TURNN OFF ALL THE WAIT TIMERS
                int waitDelay = 6 + (self.transportVessels.Count * 4);
                for (int num = self.transportVessels.Count - 1; num >= 0; num--) {
                    if (ModManager.CoopAvailable && self.transportVessels[num].creature is Player sluggo && !sluggo.isNPC && sluggo.GetCat().lastRoom == beaconRoom.roomSettings.name && !sluggo.GetCat().departedFromAltExit) { //sluggo.room == beaconRoom
                        self.transportVessels[num].wait = waitDelay;
                        //MAKE THE FIRST SLUGCAT THE CAMERA OWNER -OKAY WHY DOES THIS NOT WORK???
                        //if (waitDelay == 10)
                            //room.game.cameras[0].followAbstractCreature = sluggo.abstractCreature;
                        //ALRIGHT FINE JUST MAKE THE PLAYER WITH THE CAMERA THE FIRST ONE TO GO THROUGH
                        //if (room.game.cameras[0].followAbstractCreature == sluggo.abstractCreature) {
                        //    Debug.Log("SEND THIS CAT FIRST " + sluggo.playerState.playerNumber);
                        //    self.transportVessels[num].wait = 8;
                        //}
                        //MAKE THE EARLIEST SLUGGO TO LEAVE THE CAMERA HOLDER (WE ITERATE BACKWARDS)
                        room.game.cameras[0].followAbstractCreature = sluggo.abstractCreature;
                        sluggo.GetCat().defector = false;
                        sluggo.GetCat().forcedDefect = false;
                        sluggo.GetCat().leavingStation = waitDelay;
                        waitDelay -= 4;
                    }
                }

                WipeBeacon("ShortcutHandler_SuckInCreature"); //OKAY NOW REMOVE IT
                spotlightMode = false; //TURN THIS OFF IF IT WAS ON
                return; //WE ALREADY RAN ORIG SO DON'T DO IT AGAIN
            }

            else if (CLOptions.bringPups.Value)
            {
                //Debug.Log("TRY SCOOP 2");
                ScoopPups(room, shortCut.StartTile);
            }
        }
		
		
		//IF WE ARE TRAVELING WITH PUPS, BRING THEM WITH US
		else if (CLOptions.bringPups.Value && creature is Player player2 && !player2.isNPC && validShortType) //(!ModManager.CoopAvailable || Custom.rainWorld.options.JollyPlayerCount <= 1) &&
        {
            ScoopPups(room, shortCut.StartTile);
		}
        
        //IF IT'S A NORMAL SHORTCUT, GIVE US A WAIT TIME ANYWAYS
        //if (creature is Player && ModManager.CoopAvailable && Custom.rainWorld.options.JollyPlayerCount > 1 && shortCut.shortCutType == ShortcutData.Type.Normal)
        //    self.entranceNode.

        orig(self, creature, room, shortCut);
    }


    private void ShortCutVessel_ctor(On.ShortcutHandler.ShortCutVessel.orig_ctor orig, ShortcutHandler.ShortCutVessel self, IntVector2 pos, Creature creature, AbstractRoom room, int wait) {

        if (CLOptions.waitForAll.Value && creature is Player player  && !player.isNPC && wait > 0 && player.enteringShortCut == shortCutBeacon) {
            wait *= 1000;
        }
        orig(self, pos, creature, room, wait);
    }


    public static void ShowDepartProgressBars(RoomCamera self)
    {
        int highestForceDepart = 0;
        Color color = Color.white;
        for (int i = 0; i < self.room.game.Players.Count; i++)
        {
            Player plr = self.room.game.Players[i].realizedCreature as Player;
            if (plr != null && plr.inShortcut && plr.GetCat().forceDepart > highestForceDepart)
            {
                highestForceDepart = plr.GetCat().forceDepart;
                color = PlayerGraphics.JollyColor(plr.playerState.playerNumber, 0);
            }
        }
        if (highestForceDepart > 0)
        {
            IntVector2 pos = shortCutBeacon;
            Vector2 barSize = new Vector2((2 + highestForceDepart) * 2f, 5);
            self.room.AddObject(new ScreenGraphicDot(pos.ToVector2() * 20f, barSize, color));
            //Debug.Log("BARSIZE " + barSize.x);
        }
    }

    private void WarpPoint_NewWorldLoaded_Room(On.Watcher.WarpPoint.orig_NewWorldLoaded_Room orig, Watcher.WarpPoint self, Room newRoom)
    {
        orig(self, newRoom);
        WipeBeacon("WarpPoint.NewWorldLoaded"); //WIPE THE OLD BEACON AS WE ENTER A PORTAL
    }


    private void RainWorldGameOnShutDownProcess(On.RainWorldGame.orig_ShutDownProcess orig, RainWorldGame self)
    {
        orig(self);
        ClearMemory();
    }
    private void GameSessionOnctor(On.GameSession.orig_ctor orig, GameSession self, RainWorldGame game)
    {
        orig(self, game);
        ClearMemory();
    }

    #region Helper Methods

    private void ClearMemory()
    {
        //If you have any collections (lists, dictionaries, etc.)
        //Clear them here to prevent a memory leak
        //YourList.Clear();
    }

    #endregion
}

//NEW GRAPHIC
public class ScreenGraphicDot : CosmeticSprite
{
    public ScreenGraphicDot(Vector2 pos, Vector2 size, Color color)
    {
        this.lastPos = pos;
        this.pos = pos + new Vector2 (0, 25f);
        this.life = 2f;
        this.size = size;
        this.color = color;
    }

    public override void Update(bool eu)
    {
        base.Update(eu);
        this.life -= 1f;
        if (this.life < 0f)
        {
            this.Destroy();
        }
    }

    public override void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
    {
        sLeaser.sprites = new FSprite[1];
        sLeaser.sprites[0] = new FSprite("pixel", true);
        sLeaser.sprites[0].color = this.color; // Color.white;
        sLeaser.sprites[0].scaleX = this.size.x;
        sLeaser.sprites[0].scaleY = this.size.y;
        this.AddToContainer(sLeaser, rCam, rCam.ReturnFContainer("Foreground")); //Midground
    }

    public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        sLeaser.sprites[0].x = Mathf.Lerp(this.lastPos.x, this.pos.x, timeStacker) - camPos.x;
        sLeaser.sprites[0].y = Mathf.Lerp(this.lastPos.y, this.pos.y, timeStacker) - camPos.y;

        //LETS TINKER WITH THIS A BIT. MAYBE THEY'D LOOK BETTER AS ALL PIXELS
        //sLeaser.sprites[0].element = Futile.atlasManager.GetElementWithName("pixel");
        //sLeaser.sprites[0].color = new Color(1f, 1f, 1f);

        base.DrawSprites(sLeaser, rCam, timeStacker, camPos);
    }

    public override void ApplyPalette(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, RoomPalette palette)
    {
    }

    public override void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer newContatiner)
    {
        base.AddToContainer(sLeaser, rCam, newContatiner);
    }

    public float life;
    public Color color;
    public Vector2 size;
}


public static class PipeStatusClass
{
    public class PipeStatus
    {
        // Define your variables to store here!
        public string pipeType;
        //public Room lastRoom;
        public string lastRoom;
		public bool defector;
        public bool forcedDefect;
        public int leavingStation;
        public int skipCamTrigger;
		public int forceDepart;
		public int noCam;
        public int camProtection;
        public bool deniedSplitCam;
        public int justDefected; //GIVE THE CAMERA A FEW TICKS TO MOVE OUT OF RANGE OF THE NEWLY ADDED DEFECTOR SO THEY DON'T GET ADDED BACK IN ON THE SAME TICK
        public bool departedFromAltExit; //TO HANDLE AN EXCEPTION WHERE EVERY OTHER PLAYER WAS WAITING IN THE WARP PIPE AND THE LAST PLAYER ENTERS A DIFFERENT PIPE
        public Vector2 bodyPosMemory; //REMEMBER WHERE THIS PLAYER WAS BEFORE WE TELEPORT THEM FOR CAMERASCROLL
        public bool firstInAPipe;
        public bool stripMyDeniedSplitcam;

        public PipeStatus()
        {
            // Initialize your variables here! (Anything not added here will be null or false or 0 (default values))
            this.pipeType = "untubed";
            this.lastRoom = "";
			this.defector = false; //ACTUALLY I DON'T THINK WE EVEN USE THIS
            this.forcedDefect = false;
			this.forceDepart = 0;
			this.bodyPosMemory = new Vector2(0, 0);
        }
    }

    // This part lets you access the stored stuff by simply doing "self.GetCat()" in Plugin.cs or everywhere else!
    private static readonly ConditionalWeakTable<Player, PipeStatus> CWT = new();
    public static PipeStatus GetCat(this Player player) => CWT.GetValue(player, _ => new());
}