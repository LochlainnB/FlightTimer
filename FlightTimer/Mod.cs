using MelonLoader;
using RumbleModdingAPI;
using UnityEngine;
using TMPro;
using HarmonyLib;
using System;
using System.Collections.Generic;
using RUMBLE.MoveSystem;

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
            parts.Add(timeSpan.Seconds.ToString(parts.Count == 0 ? "D1" : "D2") + "." + timeSpan.Milliseconds.ToString("D3"));
            if (parts.Count == 1)
                parts[0] += "s";

            return string.Join(":", parts);
        }

        // How much to move the timer display relative to the player's hand
        private static readonly Vector3 relativeTranslate = new Vector3(-0.03f, -0.02f, 0.0f);
        // How much to move the timer display globally
        private static readonly Vector3 globalTranslate = new Vector3(0.0f, 0.15f, 0.0f);
        // Names of objects that the player can land on without stopping the timer
        private static readonly HashSet<string> vehicles = new HashSet<string> { "Disc", "Pillar", "Ball", "RockCube", "Wall", "LargeRock", "SmallRock", "BoulderBall" };
        // Collisions between the player and an object with a normal.y greater than this value will be considered landings
        private static readonly float maxNormalY = 0.5f;

        private static double timer = 0.0;
        private static bool isFlying = false;

        private static GameObject timerObject;
        private static TextMeshPro timerText;

        // Detect jumps and start the timer
        [HarmonyPatch(typeof(RUMBLE.MoveSystem.Stack), "Execute")]
        private static class Stack_Execute_Patch
        {
            private static void Postfix(RUMBLE.MoveSystem.Stack __instance, IProcessor processor)
            {
                if (__instance.cachedName == "Jump" && !isFlying && processor.Cast<PlayerStackProcessor>() == Calls.Players.GetLocalPlayer().Controller.GetSubsystem<PlayerStackProcessor>())
                {
                    isFlying = true;
                    timer = 0.0;
                    if (timerObject != null)
                    {
                        timerText.text = FormatTimeSpan(new TimeSpan(0));
                        timerText.color = Color.white;
                        timerText.maxVisibleCharacters = 999;
                    }
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
                    HandleLanding();
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
                        if (collision.contacts[i].normal.y > maxNormalY)
                        {
                            HandleLanding();
                        }
                    }
                }
            }
        }

        // Handle landings
        private static void HandleLanding()
        {
            MelonLogger.Msg("Flight time: " + FormatTimeSpan(TimeSpan.FromSeconds(timer)));
            isFlying = false;
            if (timerObject != null)
            {
                timerText.color = Color.green;
            }
        }

        // Create the timer display
        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (sceneName == "Gym" && timerObject == null)
            {
                timerObject = new GameObject("FlightTimer");
                GameObject.DontDestroyOnLoad(timerObject);

                timerText = timerObject.AddComponent<TextMeshPro>();
                timerText.text = FormatTimeSpan(new TimeSpan(0));
                foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (font.name == "GOODDP__ SDF Global Text Material")
                    {
                        timerText.font = font;
                        break;
                    }
                }
                timerText.fontSize = 0.5f;
                timerText.color = Color.white;
                timerText.alignment = TextAlignmentOptions.Center;
                timerText.enableWordWrapping = false;
                timerText.maxVisibleCharacters = 0;
            }
        }

        // Update the timer & display
        public override void OnUpdate()
        {
            if (isFlying)
            {
                timer += Time.deltaTime;
                if (timerObject != null)
                    timerText.text = FormatTimeSpan(TimeSpan.FromSeconds(timer));
            }
            if (timerObject != null && Calls.Players.GetLocalPlayer().Controller != null)
            {
                Transform leftHandTransform = Calls.Players.GetLocalPlayer().Controller.gameObject.transform.GetChild(1).GetChild(1);
                timerObject.transform.position = leftHandTransform.position;
                timerObject.transform.Translate(relativeTranslate, leftHandTransform);
                timerObject.transform.Translate(globalTranslate, Space.World);
                timerObject.transform.rotation = Quaternion.LookRotation(timerObject.transform.position - Camera.main.transform.position);
            }
        }
    }
}
