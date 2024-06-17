using MelonLoader;
using UnityEngine;
using Il2CppTMPro;
using HarmonyLib;
using System;
using System.Collections.Generic;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players.Subsystems;

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
        private static bool takeoffChecked = false;

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
                    takeoffChecked = false;
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
        [HarmonyPatch(typeof(PlayerMovement), "OnBecameGrounded")]
        private static class PlayerMovement_OnBecameGrounded_Patch
        {
            private static void Postfix(PlayerMovement __instance)
            {
                if (isFlying && __instance == PlayerManager.instance.localPlayer.Controller.GetSubsystem<PlayerMovement>())
                {
                    HandleLanding();
                }
            }
        }

        // Detect landings (on objects) and stop the timer
        [HarmonyPatch(typeof(Il2CppRUMBLE.Physics.Utility.PlayAudioOnImpact), "OnCollisionEnter")]
        private static class PlayAudioOnImpact_OnCollisionEnter_Patch
        {
            private static void Postfix(Il2CppRUMBLE.Physics.Utility.PlayAudioOnImpact __instance, Collision collision)
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
            
            //deprecated overloads
            displayTimer = settings.AddToList("Display Timer", true, 0, "Disable to hide the timer. Times will still be logged if \"Log Times\" is true.", new RumbleModUI.Tags());
            logTimes = settings.AddToList("Log Times", true, 0, "Enable to log flight times to the console.", new RumbleModUI.Tags());
            swapHand = settings.AddToList("Swap Hand", false, 0, "Enable to display the timer on the right hand instead of the left.", new RumbleModUI.Tags());
            displayAboveHealth = settings.AddToList("Display Above Health", false, 0, "Enable to display the timer above the health bar instead of either hand.", new RumbleModUI.Tags());
            //new overloads

            settings.GetFromFile();

            displayTimer.SavedValueChanged += OnDisplayTimerChange;
            displayAboveHealth.SavedValueChanged += OnDisplayAboveHealthChanged;

            RumbleModUI.UI.instance.UI_Initialized += OnUIInit;
        }

        // Display settings
        public static void OnUIInit()
        {
            RumbleModUI.UI.instance.AddMod(settings);
        }

        // Hide/show timer
        public static void OnDisplayTimerChange(object sender, EventArgs args)
        {
            if (timerObject != null)
            {
                if (((RumbleModUI.ValueChange<bool>)args).Value)
                    timerText.maxVisibleCharacters = 999;
                else
                    timerText.maxVisibleCharacters = 0;
            }
        }

        // Change display parent
        public static void OnDisplayAboveHealthChanged(object sender, EventArgs args)
        {
            if (timerObject != null)
            {
                if (((RumbleModUI.ValueChange<bool>)args).Value)
                    healthBar = null;
                else
                    timerObject.transform.parent = null;
            }
        }

        // Create the timer display
        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            healthBar = null;

            if (timerObject == null)
            {
                timerObject = new GameObject("FlightTimer");

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
                if (!(bool)displayTimer.SavedValue)
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

                // Check for bugged takeoff
                if (!takeoffChecked && timer > 1.0)
                {
                    takeoffChecked = true;
                    if (PlayerManager.instance.localPlayer.Controller.GetSubsystem<PlayerMovement>().IsGrounded())
                        HandleLanding();
                }
            }
            if (timerObject != null && PlayerManager.instance != null && PlayerManager.instance.localPlayer != null && PlayerManager.instance.localPlayer.Controller != null)
            {
                if (!(bool)displayAboveHealth.SavedValue)
                {
                    Transform handTransform = PlayerManager.instance.localPlayer.Controller.gameObject.transform.GetChild(1).GetChild((bool)swapHand.SavedValue ? 2 : 1);
                    timerObject.transform.position = handTransform.position;
                    timerObject.transform.Translate((bool)swapHand.SavedValue ? rightRelativeTranslate : leftRelativeTranslate, handTransform);
                    timerObject.transform.Translate(globalTranslate, Space.World);
                }
                else
                {
                    if (healthBar == null)
                    {
                        healthBar = GameObject.Find("Health");
                        if (healthBar != null)
                        {
                            timerObject.transform.parent = healthBar.transform.GetChild(1);
                            timerObject.transform.localPosition = healthRelativeTranslate;
                        }
                    }
                }
                timerObject.transform.rotation = Quaternion.LookRotation(timerObject.transform.position - Camera.main.transform.position);
            }
        }
    }
}
