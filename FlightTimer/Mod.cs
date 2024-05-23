using MelonLoader;
using RumbleModdingAPI;
using UnityEngine;
using HarmonyLib;
using System;

namespace FlightTimer
{
    public class Mod : MelonMod
    {
        private static double timer = 0.0;
        private static bool isFlying = false;

        // Detect jumps and start the timer
        [HarmonyPatch(typeof(RUMBLE.MoveSystem.Stack), "Execute")]
        private static class Stack_Execute_Patch
        {
            private static void Postfix(RUMBLE.MoveSystem.Stack __instance, RUMBLE.MoveSystem.StackConfiguration configuration)
            {
                if (__instance.cachedName == "Jump" && !isFlying && !configuration.isRemoteStack)
                {
                    MelonLogger.Msg("Player is flying!");
                    isFlying = true;
                    timer = 0.0;
                }
            }
        }

        // Detect landings and stop the timer
        [HarmonyPatch(typeof(RUMBLE.Players.Subsystems.PlayerMovement), "OnBecameGrounded")]
        private static class PlayerMovement_OnBecameGrounded_Patch
        {
            private static void Postfix(RUMBLE.Players.Subsystems.PlayerMovement __instance)
            {
                if (isFlying && __instance == Calls.Players.GetLocalPlayer().Controller.GetSubsystem<RUMBLE.Players.Subsystems.PlayerMovement>())
                {
                    MelonLogger.Msg("Player has landed.");
                    TimeSpan formattedTime = TimeSpan.FromSeconds(timer);
                    MelonLogger.Msg("Flight time: " + formattedTime.ToString());
                    isFlying = false;
                }
            }
        }

        // Update the timer if airborne
        public override void OnUpdate()
        {
            if (isFlying)
                timer += Time.deltaTime;
        }
    }
}
