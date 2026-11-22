using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace UnlockTree
{
    [BepInPlugin(Guid, "Unlock Tree", Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "okothings.unlocktree";
        public const string Version = "0.2.0";
        internal static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<Vector2> ButtonOffset;
        internal static ConfigEntry<bool> DevMode;
        Harmony _harmony;

        void Awake()
        {
            Instance = this; Log = Logger;
            ToggleKey = Config.Bind("General", "ToggleKey", new KeyboardShortcut(KeyCode.F7), "Open / close the Unlock Tree window");
            DevMode = Config.Bind("Dev", "DevMode", false, "Show the Arrange tool: lay out every node of every page by hand. The layout is saved to unlocktree.layout.json in this folder; deploy.sh ships it inside the mod");
            ButtonOffset = Config.Bind("General", "InventoryButtonOffset", Vector2.zero, "Move the inventory button if it overlaps another mod's button");
            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("Unlock Tree loaded");
        }

        // Unloaded only by ScriptEngine's hot reload (dev): remove our patches and UI so the new copy starts clean.
        void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            SagaWindow.Close();
            SagaWindow.Destroy();
            InventoryButton.Destroy();
        }

        void Update()
        {
            if (Player.m_localPlayer == null) { SagaWindow.Close(); return; }
            if (ToggleKey.Value.IsDown() && !Console.IsVisible() && !(Chat.instance != null && Chat.instance.HasFocus()) && !TextInput.IsVisible())
                SagaWindow.Toggle();
            else if (SagaWindow.IsOpen && Input.GetKeyDown(KeyCode.Escape) && !Ui.ConsumeEscape())
                SagaWindow.Close();
        }

        void LateUpdate() => Ui.WasTyping = Ui.Typing;
    }

    // While the search box has focus, the game must treat keys as typing: it already ignores its hotkeys
    // (map, inventory, chat, emotes...) whenever a text input or the chat has focus.
    [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
    static class TypingIsTextInput { static void Postfix(ref bool __result) { __result |= Ui.Typing; } }
    [HarmonyPatch(typeof(Chat), nameof(Chat.HasFocus))]
    static class TypingIsChatFocus { static void Postfix(ref bool __result) { __result |= Ui.Typing; } }

    // Input blocking and the free cursor come from Jotunn's GUIManager.BlockInput (see SagaWindow).

    // Escape must close our window, not open the pause menu.
    [HarmonyPatch(typeof(Menu), "Update")]
    static class KeepMenuClosed { static bool Prefix() => !SagaWindow.IsOpen && !SagaWindow.ClosedThisFrame; }
}
