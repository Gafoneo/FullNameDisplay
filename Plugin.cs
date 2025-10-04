using BepInEx;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using Steamworks;
using Steamworks.Data;
using System.Linq;
using UnityEngine;
using Unity.Netcode;

namespace FullNameDisplay;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class FullNameDisplay : BaseUnityPlugin
{
    internal static new ManualLogSource Logger = null!;

    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        Harmony harmony = new(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();
        Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} is loaded!");
    }
}

[HarmonyPatch]
internal static class NameSanitizePatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerControllerB), "SendNewPlayerValuesClientRpc")]
    public static bool Prefix4(ulong[] playerSteamIds, PlayerControllerB __instance)
    {
        var that = Traverse.Create(__instance);
        NetworkManager networkManager = __instance.NetworkManager;
        if (networkManager == null || !networkManager.IsListening) return false;
        int __rpc_exec_stage = that.Field("__rpc_exec_stage").GetValue<int>();
        if (__rpc_exec_stage != 1 && (networkManager.IsServer || networkManager.IsHost))
        {
            ClientRpcParams clientRpcParams = default;
            FastBufferWriter bufferWriter = that
                .Method("__beginSendClientRpc", [956616685u, clientRpcParams, RpcDelivery.Reliable])
                .GetValue<FastBufferWriter>();
            bool value = playerSteamIds != null;
            bufferWriter.WriteValueSafe(in value, default);
            if (value) bufferWriter.WriteValueSafe(playerSteamIds, default);

            that.Method("__endSendClientRpc", [bufferWriter, 956616685u, clientRpcParams, RpcDelivery.Reliable]).GetValue();
        }
        if (__rpc_exec_stage != 1 || (!networkManager.IsClient && !networkManager.IsHost))
        {
            return false;
        }
        that.Field("__rpc_exec_stage").SetValue(0);
        for (int i = 0; i < playerSteamIds.Length; i++)
        {
            if (__instance.playersManager.allPlayerScripts[i].isPlayerControlled || __instance.playersManager.allPlayerScripts[i].isPlayerDead)
            {
                string input = new Friend(playerSteamIds[i]).Name;
                if (input.Length == 0) input = "Nameless";
                __instance.playersManager.allPlayerScripts[i].playerSteamId = playerSteamIds[i];
                __instance.playersManager.allPlayerScripts[i].playerUsername = input;
                __instance.playersManager.allPlayerScripts[i].usernameBillboardText.text = input;
                string playerName = input;
                int numberOfDuplicateNamesInLobby = that.Method("GetNumberOfDuplicateNamesInLobby").GetValue<int>();
                if (numberOfDuplicateNamesInLobby > 0)
                {
                    playerName = $"{input} #{numberOfDuplicateNamesInLobby}";
                }
                __instance.quickMenuManager.AddUserToPlayerList(playerSteamIds[i], playerName, i);
                StartOfRound.Instance.mapScreen.radarTargets[i].name = playerName;
            }
        }
        StartOfRound.Instance.StartTrackingAllPlayerVoices();
        if (GameNetworkManager.Instance != null && GameNetworkManager.Instance.localPlayerController != null)
        {
            Traverse.Create(GameNetworkManager.Instance.localPlayerController).Field("updatePositionForNewlyJoinedClient").SetValue(true);
        }
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameNetworkManager), "SteamMatchmaking_OnLobbyMemberJoined")]
    public static bool Prefix5(Lobby _, Friend friend, GameNetworkManager __instance)
    {
        var Instance = GameNetworkManager.Instance;

        if (Instance.currentLobby.HasValue)
        {
            Friend[] array = Instance.currentLobby.Value.Members.ToArray();
            if (array != null)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    if (!__instance.steamIdsInLobby.Contains(array[i].Id))
                    {
                        __instance.steamIdsInLobby.Add(array[i].Id);
                    }
                }
            }
        }
        Debug.Log($"Player joined w steamId: {friend.Id}");
        if (StartOfRound.Instance != null)
        {
            QuickMenuManager quickMenuManager = UnityEngine.Object.FindObjectOfType<QuickMenuManager>();
            quickMenuManager?.AddUserToPlayerList(friend.Id, friend.Name, StartOfRound.Instance.connectedPlayersAmount);
        }
        return false;
    }
}
