using ImprovedInput;
using UnityEngine;
using RWCustom;

using static CoopLeash.PlayerMod;

namespace CoopLeash;

public static class RWInputMod {

    public static PlayerKeybind warp_keybinding = null!;

    public static void Initialize_Custom_Keybindings() {
        if (warp_keybinding != null) return;

        // initialize after ImprovedInput has;
        warp_keybinding = PlayerKeybind.Register("StickTogether-Warp", "Stick Together", Custom.rainWorld.inGameTranslator.Translate("Pipe Warp"), KeyCode.None, KeyCode.None);
        warp_keybinding.HideConflict = other_keybinding => warp_keybinding.Can_Hide_Conflict_With(other_keybinding);
    }

    public static bool Can_Hide_Conflict_With(this PlayerKeybind keybinding, PlayerKeybind other_keybinding) {
        for (int player_index_a = 0; player_index_a < maximum_number_of_players; ++player_index_a) {
            for (int player_index_b = player_index_a; player_index_b < maximum_number_of_players; ++player_index_b) {
                if (!keybinding.ConflictsWith(player_index_a, other_keybinding, player_index_b)) continue;
                if (player_index_a != player_index_b) return false;

                // this is the same as having being Unbound() for the current
                // custom keybindings;
                if (other_keybinding == PlayerKeybind.Map) continue;

                if (other_keybinding == warp_keybinding) continue;
                return false;
            }
        }
        return true;
    }

    public static InputPackageMod Get_Input(Player player) {
        InputPackageMod custom_input = new();
        int player_number = player.playerState.playerNumber;

        if (warp_keybinding.Unbound(player_number)) 
        {
            //custom_input.warp_btn = player.input[0].mp;
            custom_input.warp_btn = RWInput.CheckSpecificButton((player.State as PlayerState).playerNumber, 11, Custom.rainWorld);
        } else {
            custom_input.warp_btn = warp_keybinding.CheckRawPressed(player_number);
        }

        return custom_input;
    }
}