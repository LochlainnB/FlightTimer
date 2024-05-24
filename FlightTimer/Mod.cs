using MelonLoader;
using RumbleModdingAPI;
using UnityEngine;
using HarmonyLib;
using System;
using System.Collections.Generic;

namespace FlightTimer
{
    public class Mod : MelonMod
    {
        // Format a TimeSpan for display
        public static string FormatTimeSpan(TimeSpan timeSpan)
        {
            List<string> parts = new List<string>();

            if (timeSpan.Days > 0)
                parts.Add(timeSpan.Days.ToString());
            if (timeSpan.Hours > 0 || parts.Count > 0)
                parts.Add(timeSpan.Hours.ToString("D2"));
            if (timeSpan.Minutes > 0 || parts.Count > 0)
                parts.Add(timeSpan.Minutes.ToString("D2"));
            parts.Add(timeSpan.Seconds.ToString("D1") + "." + timeSpan.Milliseconds.ToString("D3"));
            if (parts.Count == 1)
                parts[0] += "s";

            return string.Join(":", parts);
        }

        private static double timer = 0.0;
        private static bool isFlying = false;
        private static readonly HashSet<string> vehicles = new HashSet<string> { "Disc", "Pillar", "Ball", "RockCube", "Wall", "LargeRock", "SmallRock", "BoulderBall" };

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

        // Detect landings (on ground) and stop the timer
        [HarmonyPatch(typeof(RUMBLE.Players.Subsystems.PlayerMovement), "OnBecameGrounded")]
        private static class PlayerMovement_OnBecameGrounded_Patch
        {
            private static void Postfix(RUMBLE.Players.Subsystems.PlayerMovement __instance)
            {
                if (isFlying && __instance == Calls.Players.GetLocalPlayer().Controller.GetSubsystem<RUMBLE.Players.Subsystems.PlayerMovement>())
                {
                    MelonLogger.Msg("Player has landed.");
                    TimeSpan formattedTime = TimeSpan.FromSeconds(timer);
                    MelonLogger.Msg("Flight time: " + FormatTimeSpan(formattedTime));
                    isFlying = false;
                }
            }
        }

        // Detect landings (on objects) and stop the timer
        [HarmonyPatch(typeof(RUMBLE.Physics.Utility.PlayAudioOnImpact), "OnCollisionEnter")]
        private static class PlayAudioOnImpact_OnCollisionEnter_Patch
        {
            private static void Postfix(RUMBLE.Physics.Utility.PlayAudioOnImpact __instance, Collision collision)
            {
                if (isFlying && collision.contacts[0].thisCollider == Calls.Players.GetLocalPlayer().Controller.GetSubsystem<RUMBLE.Players.Subsystems.PlayerPhysics>().pillBodyCollider && !vehicles.Contains(collision.gameObject.name))
                {
                    for (int i = 0; i < collision.contactCount; i++)
                    {
                        if (collision.contacts[i].normal.y > 0.5f)
                        {
                            MelonLogger.Msg("Player has landed on " + collision.gameObject.name);
                            TimeSpan formattedTime = TimeSpan.FromSeconds(timer);
                            MelonLogger.Msg("Flight time: " + FormatTimeSpan(formattedTime));
                            isFlying = false;
                            return;
                        }
                    }
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
