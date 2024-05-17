using MelonLoader;
using RumbleModdingAPI;
using UnityEngine;
using HarmonyLib;

namespace FlightTimer
{
    public class Mod : MelonMod
    {
        private static double timer = 0.0;
        private static bool isFlying = false;

        // Detect jumps and start the timer
        [HarmonyPatch(typeof(RUMBLE.Players.Subsystems.PlayerPoseSystem), "OnPoseCompleted")]
        private static class PlayerPoseSystem_OnPoseCompleted_Patch
        {
            private static void Postfix(RUMBLE.Players.Subsystems.PlayerPoseSystem __instance, RUMBLE.Poses.PoseData pose)
            {
                if (pose.name == "Poses_Jump" && !isFlying)
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
                if (isFlying)
                {
                    MelonLogger.Msg("Player has landed.");
                    MelonLogger.Msg("Flight time: " + timer + " seconds.");
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
