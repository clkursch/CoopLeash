using BepInEx.Logging;
using Menu.Remix.MixedUI;
using Menu.Remix.MixedUI.ValueTypes;
using UnityEngine;

namespace CoopLeash;

public class CLOptions : OptionInterface
{
    private readonly ManualLogSource Logger;

    public CLOptions(CoopLeash modInstance, ManualLogSource loggerSource)
    {
        Logger = loggerSource;
        PlayerSpeed = this.config.Bind<float>("PlayerSpeed", 1f, new ConfigAcceptableRange<float>(0f, 100f));
		CLOptions.proxDist = this.config.Bind<int>("proxDist", 25, new ConfigAcceptableRange<int>(5, 100));
		CLOptions.camPenalty = this.config.Bind<int>("camPenalty", 0, new ConfigAcceptableRange<int>(0, 10));
        CLOptions.zoomLimit = this.config.Bind<float>("zoomLimit", 0.75f, new ConfigAcceptableRange<float>(0.5f, 1.0f));
        CLOptions.waitForAll = this.config.Bind<bool>("waitForAll", true);
        CLOptions.allowForceDepart = this.config.Bind<bool>("allowForceDepart", true);
        CLOptions.allowDeadCam = this.config.Bind<bool>("allowDeadCam", false);
        CLOptions.allowSplitUp = this.config.Bind<bool>("allowSplitUp", true);
        CLOptions.warpButton = this.config.Bind<bool>("warpButton", true);
		CLOptions.bodyCountReq = this.config.Bind<bool>("bodyCountReq", false);
		CLOptions.proximityReq = this.config.Bind<bool>("proximityReq", false);
        CLOptions.quickPiggy = this.config.Bind<bool>("quickPiggy", true);
        CLOptions.smartCam = this.config.Bind<bool>("smartCam", true);
		CLOptions.bringPups = this.config.Bind<bool>("bringPups", true);
    }

    public readonly Configurable<float> PlayerSpeed;
	public static Configurable<int> proxDist;
	public static Configurable<int> camPenalty;
    public static Configurable<float> zoomLimit;
    public static Configurable<bool> waitForAll;
    public static Configurable<bool> allowForceDepart;
    public static Configurable<bool> allowDeadCam;
    public static Configurable<bool> allowSplitUp;
    public static Configurable<bool> warpButton;
	public static Configurable<bool> bodyCountReq;
	public static Configurable<bool> proximityReq;
    public static Configurable<bool> quickPiggy;
    public static Configurable<bool> smartCam;
	public static Configurable<bool> bringPups;

    private UIelement[] UIArrPlayerOptions;

	public OpSlider pDistOp;
    public OpFloatSlider pZoomOp;
    public OpCheckBox mpBox1;
    public OpCheckBox mpBox2;
	public OpCheckBox mpBox3;
	public OpCheckBox mpBox5;
    public OpCheckBox mpBox6;
    public OpCheckBox mpBox7;
    public OpCheckBox mpBox8;
    public OpCheckBox mpBox10;
    public OpLabel lblOp1;


    public override void Initialize()
    {
        var opTab = new OpTab(this, "Options");
        this.Tabs = new[]
        {
            opTab
        };
		
		//float startY = 550f + 60f;
   //     UIArrPlayerOptions = new UIelement[]
   //     {
			////new OpLabel(10f, startY, "Options", true),
   //         new OpLabel(10f, startY - 30f, "Player run speed factor"),
   //         new OpUpdown(PlayerSpeed, new Vector2(10f,startY - 60f), 100f, 1),
            
   //         new OpLabel(10f, startY - 100f, "Gotta go fast!", false){ color = new Color(0.2f, 0.5f, 0.8f) }
   //     };
   //     opTab.AddItems(UIArrPlayerOptions);
		
		
		//I DO THINGS MY WAY
		float lineCount = 580;
		int margin = 20;
		string dsc = "";
		
		// margin += 150;
		lineCount -= 60;
		//OpCheckBox mpBox1;
		dsc = Translate("Players must wait for everyone before leaving a room");
		Tabs[0].AddItems(new UIelement[]
		{
			mpBox1 = new OpCheckBox(CLOptions.waitForAll, new Vector2(margin, lineCount))
			{description = dsc},
			new OpLabel(mpBox1.pos.x + 30, mpBox1.pos.y+3, Translate("Wait for Everyone"))
			{description = dsc},
            new OpLabel(165, 575, Translate("Stick Together Options"), bigText: true)
            {alignment = FLabelAlignment.Center},
        });

        OpCheckBox mpBox4;
        dsc = Translate("Jump and grab a standing player to piggyback onto them");
        Tabs[0].AddItems(new UIelement[]
        {
            mpBox4 = new OpCheckBox(CLOptions.quickPiggy, new Vector2(margin  + 300, lineCount))
            {description = dsc},
            new OpLabel(mpBox4.pos.x + 30, mpBox4.pos.y+3, Translate("Quick Piggyback"))
            {description = dsc}
        });


        lineCount -= 50;
        dsc = Translate("Allow players to enter a pipe if a warp beacon is already active on a different pipe in the room");
        Tabs[0].AddItems(new UIelement[]
        {
            mpBox10 = new OpCheckBox(CLOptions.allowSplitUp, new Vector2(margin, lineCount))
            {description = dsc},
            new OpLabel(mpBox10.pos.x + 30, mpBox10.pos.y+3, Translate("Allow Splitting Up"))
            {description = dsc}
        });


        dsc = Translate("Tamed lizards and slugpups will teleport to you as you enter pipes");
        Tabs[0].AddItems(new UIelement[]
        {
            mpBox7 = new OpCheckBox(CLOptions.bringPups, new Vector2(margin + 300, lineCount))
            {description = dsc},
            new OpLabel(mpBox7.pos.x + 30, mpBox7.pos.y+3, Translate("Pet Leash"))
            {description = dsc}
        });



        lineCount -= 50;
        dsc = Translate("Allow players to hold JUMP to depart through pipes without waiting for other players");
        Tabs[0].AddItems(new UIelement[]
        {
            mpBox8 = new OpCheckBox(CLOptions.allowForceDepart, new Vector2(margin, lineCount))
            {description = dsc},
            new OpLabel(mpBox8.pos.x + 30, mpBox8.pos.y+3, Translate("Allow Early Departure"))
            {description = dsc}
        });


        OpCheckBox mpBox9;
        dsc = Translate("Allow dead players to request camera focus");
        Tabs[0].AddItems(new UIelement[]
        {
            mpBox9 = new OpCheckBox(CLOptions.allowDeadCam, new Vector2(margin + 300, lineCount))
            {description = dsc},
            new OpLabel(mpBox9.pos.x + 30, mpBox9.pos.y+3, Translate("Death Cam"))
            {description = dsc}
        });


        lineCount -= 50;
		dsc = Translate("Press Map to teleport to players in pipes") + Translate("(keybind can be changed with the Improved Input Config mod)");
		Tabs[0].AddItems(new UIelement[]
		{
			mpBox5 = new OpCheckBox(CLOptions.warpButton, new Vector2(margin, lineCount))
			{description = dsc},
			new OpLabel(mpBox5.pos.x + 30, mpBox5.pos.y+3, Translate("Pipe Warp Beacons"))
			{description = dsc}
		});


        
        dsc = Translate("The camera will pan towards the center of the group");
        Tabs[0].AddItems(new UIelement[]
        {
            mpBox6 = new OpCheckBox(CLOptions.smartCam, new Vector2(margin  + 300, lineCount))
            {description = dsc},
            new OpLabel(mpBox6.pos.x + 30, mpBox6.pos.y+3, Translate("Smart Camera"))
            {description = dsc},
            new OpLabel(mpBox6.pos.x + 0, mpBox6.pos.y+23, "(" + Translate("Requires SBCameraScroll mod") + ")")
        });


        dsc = Translate("Limits how far the camera can zoom out as players spread out. Lower numbers mean increased range");
        int barLngtInt = 120;
        
        Tabs[0].AddItems(new UIelement[]
        {
            pZoomOp = new OpFloatSlider(CLOptions.zoomLimit, new Vector2(margin + 450, lineCount), barLngtInt, 1, false) {description = dsc},
            new OpLabel(pZoomOp.pos.x - 20, pZoomOp.pos.y - 15, Translate("Zoom-Out Limit"), bigText: false)
            {alignment = FLabelAlignment.Center}
        });

        if (CoopLeash.camScrollEnabled)



        // margin += 150;
        lineCount -= 35;
		
		dsc = Translate("Pipes cannot be teleported to until at least half of the players are inside");
		Tabs[0].AddItems(new UIelement[]
		{
			mpBox2 = new OpCheckBox(CLOptions.bodyCountReq, new Vector2(margin + 30, lineCount))
			{description = dsc},
			new OpLabel(mpBox2.pos.x + 30, mpBox2.pos.y+3, Translate("Player count requirement"))
			{description = dsc}
		});
		
		lineCount -= 35;
		
		dsc = Translate("Teleporting to a pipe requires players to be within a certain number of tiles");
		Tabs[0].AddItems(new UIelement[]
		{
			mpBox3 = new OpCheckBox(CLOptions.proximityReq, new Vector2(margin + 30, lineCount))
			{description = dsc},
			new OpLabel(mpBox3.pos.x + 30, mpBox3.pos.y+3, Translate("Proximity requirement"))
			{description = dsc}
		});

        //IF I EVER WANT TO CHANGE THIS... OpUpdown should be the way to go
        dsc = Translate("Tiles");
        int barLngt = 90 * 3;
        float sldPad = 15;
        Tabs[0].AddItems(new UIelement[]
        {
            pDistOp = new OpSlider(CLOptions.proxDist, new Vector2(margin + 250, lineCount-5), barLngt)
            {description = dsc},
            lblOp1 = new OpLabel(pDistOp.pos.x + ((barLngt * 1) / 5f), pDistOp.pos.y + 30, Translate("Tiles"), bigText: false)
            {alignment = FLabelAlignment.Center}
			// new OpLabel(pCountOp.pos.x - sldPad, pCountOp.pos.y +5, "4"),
			// new OpLabel(pCountOp.pos.x + (barLngt * 1) + sldPad -5, pCountOp.pos.y +5, "8")
		});
		
		
		lineCount -= 45;
		dsc = Translate("Number of seconds a player must wait to call the camera again after taking it from the main group");
        barLngt = 50 * 3;
		OpSlider pCamOp;
        Tabs[0].AddItems(new UIelement[]
        {
            pCamOp = new OpSlider(CLOptions.camPenalty, new Vector2(margin + 200, lineCount), barLngt) {description = dsc},
            new OpLabel(pCamOp.pos.x - 20, pCamOp.pos.y - 15, Translate("Camera Stealing Cooldown"), bigText: false)
            {alignment = FLabelAlignment.Center}
		});


        
        int descLine = 200;
        Tabs[0].AddItems(new OpLabel(25f, descLine + 25f, "--- " + Translate("How It Works") + ": ---"));
        // Tabs[0].AddItems(new OpLabel(25f, descLine, "Press up against stuck creatures to push them. Grab them to pull"));
        // descLine -= 20;
        Tabs[0].AddItems(new OpLabel(25f, descLine, Translate("Entering a pipe will create a warp beacon for other players")));
        descLine -= 20;
        Tabs[0].AddItems(new OpLabel(25f, descLine, Translate("Tapping the MAP button will teleport you into the pipe with the beacon")));
        descLine -= 20;
        Tabs[0].AddItems(new OpLabel(25f, descLine, Translate("Tapping the MAP button again will exit the pipe")));
        descLine -= 20;
        Tabs[0].AddItems(new OpLabel(25f, descLine, Translate("Players cannot go through the beacon pipe until all players in the room enter the pipe")));
        descLine -= 20;
        Tabs[0].AddItems(new OpLabel(25f, descLine, Translate("Hold JUMP in a pipe to depart without waiting for other players")));

        descLine -= 35;
        Tabs[0].AddItems(new OpLabel(25f, descLine, Translate("(Only one beacon can exist at a time)")));
        descLine -= 20;
        Tabs[0].AddItems(new OpLabel(25f, descLine, Translate("(Entering a non-beacon pipe while a beacon exists will send you through as normal)")));


        descLine -= 35;
        Tabs[0].AddItems(new OpLabel(25f, descLine, Translate("If the SBCameraScroll mod is enabled, the camera will pan evenly between all players")));
        descLine -= 20;
        Tabs[0].AddItems(new OpLabel(25f, descLine, Translate("Tap the MAP button to toggle between group focus and solo focus")));
        descLine -= 20;
        Tabs[0].AddItems(new OpLabel(25f, descLine, Translate("Getting too far off-screen will remove you from group focus until you get close enough to re-group")));



    }

    public override void Update()
    {
        //if (((OpUpdown)UIArrPlayerOptions[2]).GetValueFloat() > 10)
        //{
        //    ((OpLabel)UIArrPlayerOptions[3]).Show();
        //}
        //else
        //{
        //    ((OpLabel)UIArrPlayerOptions[3]).Hide();
        //}

        if (this.mpBox1 != null)
        {
			bool waitForAll = this.mpBox1.GetValueBool();
			bool pipeWarping = this.mpBox5.GetValueBool();

            if (!CoopLeash.camScrollEnabled)
                this.mpBox6.greyedOut = true;


            if (!waitForAll)
			{
                this.mpBox5.greyedOut = true;
                this.mpBox8.greyedOut = true;
                this.mpBox10.greyedOut = true;
                pipeWarping = false;
            }
			else
            {
                this.mpBox5.greyedOut = false;
                this.mpBox8.greyedOut = false;
                this.mpBox10.greyedOut = false;
            }


            if (pipeWarping)
            {
                
                this.mpBox2.greyedOut = false;
                this.mpBox3.greyedOut = false;
				if(this.mpBox3.GetValueBool() == true)
				{
                    this.pDistOp.greyedOut = false;
                    //this.lblOp1.Hidden = false;
                    this.lblOp1.Show();
                }
				else
				{
                    this.pDistOp.greyedOut = true;
                    this.lblOp1.Hide();
                }
                    
            }
            else
            {
                this.mpBox2.greyedOut = true;
                this.mpBox3.greyedOut = true;
                this.pDistOp.greyedOut = true;
                this.lblOp1.Hide();
            }
        }


    }

}