using System.Collections.Generic;

namespace CoopLeash;

public static class PlayerMod {

    public static readonly int maximum_number_of_players = RainWorld.PlayerObjectBodyColors.Length;
    public static List<InputPackageMod[]> custom_input_list = null!;

    internal static void OnEnable() {
        Initialize_Custom_Inputs();
        On.Player.checkInput -= Player_CheckInput;
        On.Player.checkInput += Player_CheckInput;
    }

    public static void Initialize_Custom_Inputs() {
        if (custom_input_list != null) return;
        custom_input_list = new();

        for (int player_number = 0; player_number < maximum_number_of_players; ++player_number) {
            InputPackageMod[] custom_input = new InputPackageMod[2];
            custom_input[0] = new();
            custom_input[1] = new();
            custom_input_list.Add(custom_input);
        }
    }

    public static bool WantsToWarp(this Player player) {
        int player_number = player.playerState.playerNumber;
        if (player_number < 0) return player.input[0].mp && !player.input[1].mp;
        if (player_number >= maximum_number_of_players) return player.input[0].mp && !player.input[1].mp;

        InputPackageMod[] custom_input = custom_input_list[player_number];
        return custom_input[0].warp_btn && !custom_input[1].warp_btn;
    }

    public static bool WantsToLeavePipe(this Player player)
    {
        return RWInputMod.Get_Input(player).warp_btn;
    }

    private static void Player_CheckInput(On.Player.orig_checkInput orig, Player player) {
        // update player.input first;
        orig(player);

        int player_number = player.playerState.playerNumber;
        if (player_number < 0) return;
        if (player_number >= maximum_number_of_players) return;

        InputPackageMod[] custom_input = custom_input_list[player_number];
        custom_input[1] = custom_input[0];

        if (player.stun == 0 && !player.dead) {
            custom_input[0] = RWInputMod.Get_Input(player);
            return;
        }
        custom_input[0] = new();
    }

    public struct InputPackageMod {
        public bool warp_btn = false;
        public InputPackageMod() { }
    }
}