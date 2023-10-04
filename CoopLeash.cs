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

#pragma warning disable CS0618

[module: UnverifiableCode]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace CoopLeash;

[BepInPlugin("WillowWisp.CoopLeash", "Stick Together", "1.0.0")]

/*
Test
-Do pole plant kills count as pipes?
-Exclude slugpups from all checks


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


//fORCE CAMERA SWITCH WHEN focus enters a pipe
//New "Defector" check for players who get too far offscreen
//IF WE'RE FOLLOWING A DEFECTOR, IT'S ALWAYS A SPOTLIGHT
//Make sure you can't hop on stuck players

//Hold jump to force departure
//Scoop tamed lizards too
*/

public partial class CoopLeash : BaseUnityPlugin
{
    private CLOptions Options;
    

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

    public static bool rotundWorldEnabled = false;
	public static bool camScrollEnabled = false;
	public static bool swallowAnythingEnabled = false;

    private void OnEnable()
    {
        On.RainWorld.OnModsInit += RainWorldOnOnModsInit;
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
                {
                    rotundWorldEnabled = true;
                }
				if (ModManager.ActiveMods[i].id == "SBCameraScroll")
                {
                    camScrollEnabled = true;
                }
                if (ModManager.ActiveMods[i].id == "swalloweverything")
                    swallowAnythingEnabled = true;
            }

            //Your hooks go here
            On.RainWorldGame.ShutDownProcess += RainWorldGameOnShutDownProcess;
            On.GameSession.ctor += GameSessionOnctor;

            On.RainWorldGame.Update += RainWorldGame_Update;
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
            On.RoomCamera.Update += RoomCamera_Update;
            On.ShortcutHandler.Update += ShortcutHandler_Update;
            On.Menu.ControlMap.ctor += ControlMap_ctor;
            On.Lizard.Update += Lizard_Update;

            On.JollyCoop.JollyHUD.JollyMeter.Draw += JollyMeter_Draw;
            On.JollyCoop.JollyHUD.JollyPlayerSpecificHud.JollyPlayerArrow.Draw += JollyPlayerArrow_Draw;

            IsInit = true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            throw;
        }
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
		return (ModManager.MSC || ModManager.CoopAvailable) && self.slugOnBack != null && !self.slugOnBack.interactionLocked && self.slugOnBack.slugcat == null && (self.spearOnBack == null || !self.spearOnBack.HasASpear);
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
                && !(checkPlayer.dead || checkPlayer.dangerGraspTime > 0) //CHECK THESE NOW SINCE BEING IN A SHORTCUT NEGATES THE VALIDATION CHECK
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
        orig(self, timeStacker);
        if (!CLOptions.waitForAll.Value)
            return;
        //HIDE STACKS OF PLAYER NAMES WAITING IN PIPES SO IT'S EASIER TO TELL WHEN SOMEONE IS NOT ALL THE WAY IN
        if (self.jollyHud.RealizedPlayer != null && self.jollyHud.RealizedPlayer.inShortcut)
        {
            self.label.alpha = 0;
        }
    }



    private void Player_TriggerCameraSwitch(On.Player.orig_TriggerCameraSwitch orig, Player self)
    {
        if (CLOptions.allowDeadCam.Value == false && self.dead)
            return;

        if (camScrollEnabled == false || CLOptions.smartCam.Value == false || self.timeSinceSpawned < 10)
        {
            orig(self);
            return;
        }

        //IGNORE CASES WHERE WE WERE JUST TRYING TO USE THE WARP PIPES //(self.room != null && self.room == beaconRoom  && ValidPlayer(self)) ||
        if ( self.enteringShortCut != null || self.GetCat().skipCamTrigger > 0)
            return;
		
		bool cycleSwitch = false;

        //IF WE ARE A DEFECTOR CALLING THE CAMERA FROM SOMEONE ELSE, SPOTLIGHT US RIGHT AWAY.
        if (self.GetCat().defector || UnTubedSlugsInRoom(self.room) <= 1) //IF WE ARE THE ONLY PLAYER IN THE ROOM (BUT NOT THE ONLY ONE STILL ALIVE) ALLOW SWITCH
        {
			if (self.room == null || self.room?.game?.cameras[0]?.followAbstractCreature?.realizedCreature == self)
			{
				cycleSwitch = true;
				spotlightMode = false;
			}
			else
				spotlightMode = true;
		}
		else //ELSE. WE SHOULD JUST ALWAYS JUST TOGGLE
			spotlightMode = !spotlightMode;

        //Debug.Log("CAM SWITCH: " + self.playerState.playerNumber + " - " + cycleSwitch + "  NOW SPOTLIGHTED? " + spotlightMode);
        bool origCycle = Custom.rainWorld.options.cameraCycling;
		if (!cycleSwitch)
			Custom.rainWorld.options.cameraCycling = false; //PRETEND CYCLING DOESN'T EXIST 
        orig(self);
        Custom.rainWorld.options.cameraCycling = origCycle;
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
        }
    }

    public const int tickMax = 20;
	int tickClock = tickMax;
    public static Vector2 lastCamPos = new Vector2(0, 0);
    float turple = 1f;


    bool spotlightMode = false;
    private void RoomCamera_Update(On.RoomCamera.orig_Update orig, RoomCamera self) 
    {
        //OKAY. SO I'VE GOT A CRAZY IDEA....
        AbstractCreature origFollorCrit = null;
        Vector2 origBodyPos = new Vector2(0, 0);
		Vector2 origBodyPos2 = new Vector2(0, 0);
        bool shiftBody = false;
        bool requestSwap = false;
        AbstractCreature swapFollower = null;

        if (camScrollEnabled && CLOptions.smartCam.Value && ModManager.CoopAvailable && !self.voidSeaMode && self.followAbstractCreature != null && self.followAbstractCreature.realizedCreature != null && self.followAbstractCreature.realizedCreature is Player && self.room != null)
        {
            origFollorCrit = self.followAbstractCreature;
            origBodyPos = self.followAbstractCreature.realizedCreature.mainBodyChunk.pos;
			origBodyPos2 = self.followAbstractCreature.realizedCreature.bodyChunks[1].pos;
				
			//IF WE'RE FOLLOWING A DEFECTOR, IT'S ALWAYS A SPOTLIGHT
			if ((self.followAbstractCreature.realizedCreature as Player).GetCat().defector)
				spotlightMode = true;
				
			//THIS CHUNK IS REJOINING THE NON-SPOTLIGHT RUNNING BECAUSE FORCED DEFECTOR SPOTLIGHTS SHOULD CHECK TO AUTO REJOING
			float maxLeft = origBodyPos.x;
			float maxRight = origBodyPos.x;
			float maxUp = origBodyPos.y;
			float maxDown = origBodyPos.y;
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
					if (plr.GetCat().defector)
					{
						if (Mathf.Abs(plr.mainBodyChunk.pos.x - lastCamPos.x) < 650 && Mathf.Abs(plr.mainBodyChunk.pos.y - lastCamPos.y) < 325) //self.lastPos
                        {
                            plr.GetCat().defector = false;
                            //IF WE HAD BEEN FORCED OUT OF OUR GROUP, WE CAN AUTO-REJOIN OUT OF SPOTLIGHT MODE
                            if (plr.GetCat().forcedDefect && spotlightMode)
                            {
                                spotlightMode = false;
                                plr.GetCat().forcedDefect = false;
                            }
                            //Debug.Log("UNDEFECT");
                        }  
					}
							
					if (!plr.GetCat().defector && plr.GetCat().pipeType != "other") //SKIP THIS CHECK IF WE ARE TRANSITIONING BETWEEN ROOMS. OUR UPDATES ARE STILL FROM THE PREVIOUS ROOM
					{
						Vector2 plrPos = plr.mainBodyChunk.pos;
						if (plrPos.x < maxLeft)
							maxLeft = plrPos.x;
						if (plrPos.x > maxRight)
							maxRight = plrPos.x;
						if (plrPos.y < maxDown)
							maxDown = plrPos.y;
						if (plrPos.y > maxUp)
							maxUp = plrPos.y;
								
						//KEEP TRACK OF THESE STATS. WE'LL AVERAGE OUT THE POSITIONS OF EVERYONE SHORTLY
						totalX += plrPos.x;
						totalY += plrPos.y;
						totalCnt++;
					}

                    if (plr.GetCat().pipeType != "normal") //!plr.inShortcut
                    {
                        unPiped++; //KEEP TRACK OF THIS. WE NEED TO KNOW IF IT'S 0
                    }
                }
			}

            //FIND THE HIGHEST PLAYER ON SCREEN
            for (int i = 0; i < self.room.game.Players.Count; i++)
            {
                Player plr = self.room.game.Players[i].realizedCreature as Player;
                if (plr != null && !plr.dead && self.room.abstractRoom == self.room.game.Players[i].Room && !plr.inShortcut && !plr.GetCat().defector)
                {
                    if (unPiped > 0 && plr.mainBodyChunk.pos.y == maxUp && maxDown < maxUp - 50) //REMEMBER THIS CREATURE, WE MIGHT NEED TO SWAP TO THEM LATER
                        swapFollower ??= plr.abstractCreature; //OH IT CAN'T BE JUST ANY CREATURE. IT NEEDS TO BE THE HIGHEST CREATURE, I THINK...
                }
            }


            //SKIP THE REST OF THIS CHECK IF EVERYONE IS IN A SHORTCUT. JUST RUN IT AS NORMAL
            if (unPiped != 0)  //!spotlightMode
            {

                //IF WE'RE FOCUSED ON SOMEONE IN A PIPE, QUICKLY SWITCH IT TO THE FIRST AVAILABLE NON-PIPE PLAUER
                if ((self.followAbstractCreature.realizedCreature as Player).inShortcut)
                {
                    for (int i = 0; i < self.room.game.Players.Count; i++)
                    {
                        Player plr = self.room.game.Players[i].realizedCreature as Player;
                        if (plr != null && !plr.dead && self.room.abstractRoom == self.room.game.Players[i].Room && !plr.inShortcut)
                            swapFollower ??= plr.abstractCreature; //LITERALLY JUST ANYONE. THE GAME CAN SORT IT OUT NEXT TICK IF THAT'S A PROBLEM
                    }
                }

                //Debug.Log("RUNNING THE THING" + maxLeft + " - " + maxRight + " - " + maxUp + " - " + maxDown + " - ");

                //CHECK FOR DEFECTORS, PLAYERS WHO HAVE STRAYED TOO FAR FROM THE GROUP AVERAGE IN EITHER THE X OR Y AXIS
                float avgX = totalX / totalCnt;
                float avgY = totalY / totalCnt;
                Player mostBehindPlayer = null;
                float mostBehind = 0;
                float xLimit = 1300;
                float yLimit = 700;
                //X LIMIT
                if (Mathf.Abs(maxLeft - maxRight) > xLimit)
                {
                    for (int i = 0; i < self.room.game.Players.Count; i++)
                    { //DO IT AGAIN....
                        Player plr = self.room.game.Players[i].realizedCreature as Player;
                        if (plr != null && !plr.dead && !plr.GetCat().defector && self.room?.abstractRoom == self.room.game.Players[i].Room)
                        {
                            float myBehind = Mathf.Abs(plr.mainBodyChunk.pos.x - avgX);
                            if (plr.inShortcut || plr.dangerGraspTime > 30)
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
                        if (plr != null && !plr.dead && !plr.GetCat().defector && self.room?.abstractRoom == self.room.game.Players[i].Room)
                        {
                            float myBehind = Mathf.Abs(plr.mainBodyChunk.pos.y - avgY);
                            if (plr.inShortcut || plr.dangerGraspTime > 30)
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
                    if (!spotlightMode) //DON'T APPLY THIS IN SPOTLIGHT MODE OR IT WOULD APPLY TO PLAYERS WHO TAKE SPOTLIGHT AND WALK AWAY
                        mostBehindPlayer.GetCat().forcedDefect = true;
                    if (mostBehindPlayer.abstractCreature == self.followAbstractCreature && self.hud?.jollyMeter != null)
                        self.hud.jollyMeter.customFade = 10f; //SHOW THE HUD FOR A SECOND SO THEY KNOW THINGS CHANGED
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
                    Vector2 slingShot = adjPosition - self.followAbstractCreature.realizedCreature.mainBodyChunk.pos;
                    self.followAbstractCreature.realizedCreature.mainBodyChunk.pos += new Vector2(slingShot.x * 2f, slingShot.y * (turple)); //WAIT IM SO CONFUSED... DOES ONLY THE X NEED TO BE DOUBLED???
                    shiftBody = true;
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
            self.followAbstractCreature = swapFollower; //IT'S SAFE TO JUST CHANGE THIS WITHOUT CALLING ChangeCameraToPlayer() AS LONG AS THEY ARE IN THE SAME ROOM
        //ABSOLUTELY STUPIT

        //SHOW THE BEACON FX AROUND THE PIPE
        Creature creature = (self.followAbstractCreature != null) ? self.followAbstractCreature.realizedCreature : null;
        if (creature != null && creature is Player && beaconRoom != null && self.room == beaconRoom && tickClock <= 0) {
            float lifetime = 25f;
            float strainMag = 40f;
            IntVector2 pos = shortCutBeacon;
            self.room.AddObject(new ExplosionSpikes(self.room, pos.ToVector2() * 20f, 15, 15f, lifetime, 5.0f, strainMag, new Color(1f, 1f, 1f, 0.9f)));
            tickClock = tickMax;
        }
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
            if (slug.CanPutSlugToBack || (swallowAnythingEnabled && SlugBackCheck(slug)))
                break; //THIS IS OUR SLUG!
        }

        if (!(slug.CanPutSlugToBack || (swallowAnythingEnabled && SlugBackCheck(slug))) && slug.Consious)
            return null;

        return slug;
    }




    private void Player_GrabUpdate(On.Player.orig_GrabUpdate orig, Player self, bool eu)
    {
        orig(self, eu);

        //DON'T DO THIS IN ARENA
        if (!(self.room?.game?.session is StoryGameSession))
            return;

        //SHOULD WE ATTEMPT TO CLIMB ON SOMEONES BACK?
        if (CLOptions.quickPiggy.Value && self.input[0].pckp && !self.input[1].pckp && self.onBack == null && self.room != null && !self.isNPC && !self.pyroJumpped && !self.submerged && self.standing && self.lowerBodyFramesOffGround > 0)
        {
            //Debug.Log("ON WHO??" + self.onBack);
            float range = 20 + self.bodyChunks[1].rad;
            for (int i = 0; i < self.room.game.Players.Count; i++)
            {
                if (self.room.game.Players[i].realizedCreature != null
                    && self.room.game.Players[i].realizedCreature is Player player
                    && player != self
                    && player.room == self.room
                    && Custom.DistLess(self.bodyChunks[1].pos, player.bodyChunks[0].pos, range)
                    && player.Consious && (player.standing || player.onBack != null) //Standing OR on someones back
                )
                {
                    Player newSeat = GetSlugStackTop(player);
                    if (newSeat != null && newSeat.slugOnBack != null) //newSeat.slugOnBack SHOULD NEVER BE NULL BUT ACCORDING TO EXCEPTION LOGS IT WILL...
                    {
                        //PUT US UP THERE!
                        newSeat.bodyChunks[0].pos += Custom.DirVec(self.firstChunk.pos, newSeat.bodyChunks[0].pos) * 2f;
                        newSeat.slugOnBack.SlugToBack(self);
                        break;
                    }
                }
            }
        }
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
		
        if (self.room != null && ValidPlayer(self)) {
            
            if (CLOptions.warpButton.Value && self.room == beaconRoom)
            {
                //CHECK FOR DISTANCE REQUIREMENTS TO TELEPORT! //CHECK IF ENOUGH SLUGS ARE IN THE TUBE TO ALLOW TELEPORT
                bool distReq = !CLOptions.proximityReq.Value || Custom.DistLess(self.bodyChunks[0].pos, self.room.MiddleOfTile(shortCutBeacon), CLOptions.proxDist.Value * 20);
                bool bodyReq = !CLOptions.bodyCountReq.Value || TubedSlugs(self.room) >= UnTubedSlugsInRoom(self.room);

                //TAP MAP TO TELEPORT!
                if (distReq && bodyReq && self.onBack == null) // && !(rotundWorldEnabled && self.room.abstractRoom.shelter)
                {
                    //TELEPORT TO THE BEACON PIPE!
                    if (self.input[0].mp && self.shortcutDelay <= 0 && self.enteringShortCut == null)
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

    private void ShortcutHandler_Update(On.ShortcutHandler.orig_Update orig, ShortcutHandler self) {

        int othersInRoom = 0;
        int slugsInPipe = 0; 
        for (int num = self.transportVessels.Count - 1; num >= 0; num--) 
        {
            if (ModManager.CoopAvailable && self.transportVessels[num]?.creature is Player player && !player.isNPC && self.transportVessels[num].room == beaconRoom?.abstractRoom) {
				bool forceDepart = false;
                //IF WE PRESS THE MAP BUTTON, DUMP US OUT WHERE WE STAND! I THINK
                Room realizedRoom = self.transportVessels[num].room.realizedRoom; //DO WE REALLY NEED TO CHECK IF IT'S AN ENTRANCE?
                if (realizedRoom != null && realizedRoom.GetTile(self.transportVessels[num].pos).Terrain == Room.Tile.TerrainType.ShortcutEntrance)
				{
                    Player myPlayer = self.transportVessels[num].creature as Player;
                    if (myPlayer != null)
                    {
                        if (RWInput.CheckSpecificButton((myPlayer.State as PlayerState).playerNumber, 11, Custom.rainWorld))
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
                        if (CLOptions.allowForceDepart.Value && RWInput.CheckSpecificButton((myPlayer.State as PlayerState).playerNumber, 0, Custom.rainWorld))
                        {
                            myPlayer.GetCat().forceDepart++;
                            if (myPlayer.GetCat().forceDepart > 12)
                            {
                                self.transportVessels[num].wait = 0;
                                forceDepart = true;
                                //FORCEFULLY SET THE CAMERA TO US BECAUSE WE PROBABLY WANT IT AND ALSO TO FIX UNLOADED ROOM ISSUES
                                realizedRoom.game.cameras[0].ChangeCameraToPlayer(myPlayer.abstractCreature);
                                myPlayer.GetCat().defector = true;
                                spotlightMode = true;
                                //WIPE THE BEACON IF WE WERE THE ONLY ONE WAITING
                                if (TubedSlugs(realizedRoom) == 1)
                                    WipeBeacon("ShortcutHandler_Update");
                                myPlayer.PlayHUDSound(SoundID.MENU_Button_Standard_Button_Pressed);
                            }
                        }
                        else if (myPlayer.GetCat().forceDepart > 0)
                            myPlayer.GetCat().forceDepart--; //DECAY THE VALUE IF NOT HOLDING IT
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
                    if (CLOptions.waitForAll.Value && othersInRoom > 0 && !forceDepart)
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
        if (self is Player player && !player.isNPC)
        {
            player.GetCat().lastRoom = player.room.roomSettings.name; //WILL THIS BE NON-NULL?
			if (self.room?.shortcutData(entrancePos).shortCutType == ShortcutData.Type.Normal)
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
            if (CLOptions.allowSplitUp.Value == false && beaconRoom == self.room && shortCutBeacon != entrancePos)
            {
                self.shortcutDelay = 20;
                self.enteringShortCut = null;
                (self as Player).PlayHUDSound(SoundID.MENU_Error_Ping);
                return;//JUST CUT IT OUT! DON'T EVEN ENTER THE PIPE
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
        }
    }

    //APPARENTLY THIS DOES NOT RUN FOR CREATURES GETTING CARRIED INTO A SHORTCUT BY ANOTHER CREATURE. KEEP THAT IN MIND
    //HERES THE ORDER: SUCKINCREATURE IS CALLED ON THE MAIN CREATURE. THEN, AFTER THAT IS DONE, IT SETS Creature.inShortcut = true FOR ALL CONNECTED OBJECTS
    private void ShortcutHandler_SuckInCreature(On.ShortcutHandler.orig_SuckInCreature orig, ShortcutHandler self, Creature creature, Room room, ShortcutData shortCut) {

        
        bool validShortType = shortCut.shortCutType == ShortcutData.Type.RoomExit; // || shortCut.shortCutType == ShortcutData.Type.Normal;
        if (CLOptions.waitForAll.Value && creature is Player player && !player.isNPC && ModManager.CoopAvailable && Custom.rainWorld.options.JollyPlayerCount > 1 && validShortType) {

            //!!!! FOR THIS TO WORK! WE NEED TO TAKE INTO ACCOUNT ALL SLUGCATS THAT WERE ON OUR BACK AS WE ENTER THIS PIPE, SINCE THE GAME WON'T SEE THEM AS "IN A PIPE" YET

            //DO NOT ACTIVATE UNLESS THERE ARE OTHER SLUGCATS IN THE ROOM. AND DEACTIVATE IF WE'RE THE LAST ONE
            int othersInRoom = 0;
			int slugsInPipe = 1; //it's us
			//HERE THIS VERSION CHECKS ALL PLAYERS, NOT JUST PLAYERS IN THE ROOOM (SINCE I BELEIVE PLAYERS IN SHORTCUTS NO LONGER COUNT AS IN THE ROOM)
			for (int i = 0; i < room.game.Players.Count; i++)
            {
                if (room.game.Players[i].realizedCreature != null && room.game.Players[i].realizedCreature is Player sluggo && ValidPlayer(sluggo) && sluggo != creature && sluggo.room == creature.room)
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
                    //Debug.Log("PIPE TIME " + sluggo.playerState.playerNumber + " : " + sluggo.GetCat().lastRoom + " : " + (beaconRoom == null || sluggo.GetCat().lastRoom == beaconRoom.roomSettings.name) + " - " + (slug.inShortcut || slug == creature));
                    //NOW!... IS WE IN THE PIPE OR NOT
                    if ((slug.GetCat().pipeType == "other" || slug == creature) && (beaconRoom == null || sluggo.GetCat().lastRoom == beaconRoom.roomSettings.name)) //WE CHECK FOR == CREATURE BECAUSE THEY ARE THE ONES ENTERING THE PIPE (but arent inShortcut yet)
						slugsInPipe++;
					else
						othersInRoom++;
                }
            }

            
            //ENTERING THE PIPE WILL BREAK CAMERA PANNING BECAUSE WE CAN NO LONGER STRETCH OUR TORSO. HAND CAMERA CONTROL TO SOMEONE ELSE! SOMEONE NOT IN A PIPE
            if (creature.abstractCreature == room.game.cameras[0].followAbstractCreature && othersInRoom > 0)
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
                int waitDelay = 10;
                for (int num = self.transportVessels.Count - 1; num >= 0; num--) {
                    if (ModManager.CoopAvailable && self.transportVessels[num].creature is Player sluggo && !sluggo.isNPC && sluggo.GetCat().lastRoom == beaconRoom.roomSettings.name) { //sluggo.room == beaconRoom
                        self.transportVessels[num].wait = waitDelay;
                        //MAKE THE FIRST SLUGCAT THE CAMERA OWNER -OKAY WHY DOES THIS NOT WORK???
                        //ALRIGHT FINE JUST MAKE THE PLAYER WITH THE CAMERA THE FIRST ONE TO GO THROUGH
                        if (room.game.cameras[0].followAbstractCreature == sluggo.abstractCreature) {
                            //Debug.Log("SEND THIS CAT FIRST " + sluggo.playerState.playerNumber);
                            self.transportVessels[num].wait = 8;
                        }
						sluggo.GetCat().defector = false;
                        sluggo.GetCat().forcedDefect = false;
                        waitDelay += 2;
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


    public static IntVector2 shortCutBeacon = new IntVector2(0, 0);
    public static Room beaconRoom;

    private void ShortCutVessel_ctor(On.ShortcutHandler.ShortCutVessel.orig_ctor orig, ShortcutHandler.ShortCutVessel self, IntVector2 pos, Creature creature, AbstractRoom room, int wait) {

        if (CLOptions.waitForAll.Value && creature is Player player  && !player.isNPC && wait > 0 && player.enteringShortCut == shortCutBeacon) {
            wait *= 1000;
        }
        orig(self, pos, creature, room, wait);
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
        public int skipCamTrigger;
		public int forceDepart;

        public PipeStatus()
        {
            // Initialize your variables here! (Anything not added here will be null or false or 0 (default values))
            this.pipeType = "untubed";
            this.lastRoom = "";
			this.defector = false;
            this.forcedDefect = false;
			this.forceDepart = 0;
        }
    }

    // This part lets you access the stored stuff by simply doing "self.GetCat()" in Plugin.cs or everywhere else!
    private static readonly ConditionalWeakTable<Player, PipeStatus> CWT = new();
    public static PipeStatus GetCat(this Player player) => CWT.GetValue(player, _ => new());
}