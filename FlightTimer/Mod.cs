using MelonLoader;
using UnityEngine;
using TMPro;
using HarmonyLib;
using System;
using System.Collections.Generic;
using RUMBLE.MoveSystem;
using RUMBLE.Managers;
using RUMBLE.Players.Subsystems;

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
        private static readonly Vector3 leftRelativeTranslate = new Vector3(-0.03f, -0.02f, 0.0f);
        private static readonly Vector3 rightRelativeTranslate = new Vector3(-leftRelativeTranslate.x, leftRelativeTranslate.y, leftRelativeTranslate.z);
        // How much to move the timer display globally when attached to the player's hand
        private static readonly Vector3 globalTranslate = new Vector3(0.0f, 0.15f, 0.0f);
        // How much to move the timer display relative to the health bar
        private static readonly Vector3 healthRelativeTranslate = new Vector3(0.0f, 0.05f, 1.0f);
        // Names of objects that the player can land on without stopping the timer
        private static readonly HashSet<string> vehicles = new HashSet<string> { "Disc", "Pillar", "Ball", "RockCube", "Wall", "LargeRock", "SmallRock", "BoulderBall" };
        // Collisions between the player and an object with a normal.y greater than this value will be considered landings
        private static readonly float maxNormalY = 0.5f;

        private static RumbleModUI.Mod settings = new RumbleModUI.Mod();
        private static RumbleModUI.ModSetting<bool> displayTimer;
        private static RumbleModUI.ModSetting<bool> logTimes;
        private static RumbleModUI.ModSetting<bool> swapHand;
        private static RumbleModUI.ModSetting<bool> displayAboveHealth;


        private static double timer = 0.0;
        private static bool isFlying = false;

        private static GameObject timerObject;
        private static TextMeshPro timerText;
        private static GameObject healthBar;

        // Detect jumps and start the timer
        [HarmonyPatch(typeof(Stack), "Execute")]
        private static class Stack_Execute_Patch
        {
            private static void Postfix(Stack __instance, IProcessor processor)
            {
                if (__instance.cachedName == "Jump" && !isFlying && processor.Cast<PlayerStackProcessor>() == PlayerManager.instance.localPlayer.Controller.GetSubsystem<PlayerStackProcessor>())
                {
                    isFlying = true;
                    timer = 0.0;
                    if (timerObject != null)
                    {
                        timerText.text = FormatTimeSpan(new TimeSpan(0));
                        timerText.color = Color.white;
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
                if (isFlying && __instance == PlayerManager.instance.localPlayer.Controller.GetSubsystem<PlayerMovement>())
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
                if (isFlying && collision.contacts[0].thisCollider == PlayerManager.instance.localPlayer.Controller.GetSubsystem<PlayerPhysics>().pillBodyCollider && !vehicles.Contains(collision.gameObject.name))
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
            if ((bool)logTimes.Value)
                MelonLogger.Msg("Flight time: " + FormatTimeSpan(TimeSpan.FromSeconds(timer)));
            isFlying = false;
            if (timerObject != null)
            {
                timerText.color = Color.green;
            }
        }

        // Load settings
        public override void OnLateInitializeMelon()
        {
            settings.ModName = "FlightTimer";
            settings.ModVersion = "1.0.0";
            settings.SetFolder("FlightTimer");
            
            displayTimer = settings.AddToList("Display Timer", true, Description:"Disable to hide the timer. Times will still be logged if \"Log Times\" is true.");
            logTimes = settings.AddToList("Log Times", true, Description:"Enable to log flight times to the console.");
            swapHand = settings.AddToList("Swap Hand", false, Description:"Enable to display the timer on the right hand instead of the left.");
            displayAboveHealth = settings.AddToList("Display Above Health", false, Description:"Enable to display the timer above the health bar instead of either hand.");

            settings.GetFromFile();

            displayTimer.CurrentValueChanged += OnDisplayTimerChange;

            RumbleModUI.UI.instance.UI_Initialized += OnUIInit;
        }

        // Display settings
        public static void OnUIInit()
        {
            RumbleModUI.UI.instance.AddMod(settings);
        }

        // Hide/show timer
        public static void OnDisplayTimerChange()
        {
            if (timerObject != null)
            {
                // displayTimer.Value currently stores the OLD value. Since it is a bool, the new value must be !old
                if (!(bool)displayTimer.Value)
                    timerText.maxVisibleCharacters = 999;
                else
                    timerText.maxVisibleCharacters = 0;
            }
        }

        // Create the timer display
        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            healthBar = null;
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
                if (!(bool)displayTimer.Value)
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
            if (timerObject != null && PlayerManager.instance.localPlayer.Controller != null)
            {
                if (!(bool)displayAboveHealth.Value)
                {
                    Transform handTransform = PlayerManager.instance.localPlayer.Controller.gameObject.transform.GetChild(1).GetChild((bool)swapHand.Value ? 2 : 1);
                    timerObject.transform.position = handTransform.position;
                    timerObject.transform.Translate((bool)swapHand.Value ? rightRelativeTranslate : leftRelativeTranslate, handTransform);
                    timerObject.transform.Translate(globalTranslate, Space.World);
                }
                else
                {
                    if (healthBar == null)
                        healthBar = GameObject.Find("Health");
                    if (healthBar != null)
                    {
                        Transform healthTransform = healthBar.transform.GetChild(1);
                        timerObject.transform.position = healthTransform.position;
                        timerObject.transform.Translate(healthRelativeTranslate, healthTransform);
                        timerObject.transform.Translate(PlayerManager.instance.localPlayer.Controller.GetSubsystem<PlayerPhysics>().physicsRigidbody.velocity * Time.deltaTime, Space.World);
                    }
                }
                timerObject.transform.rotation = Quaternion.LookRotation(timerObject.transform.position - Camera.main.transform.position);
            }
        }
    }
}
