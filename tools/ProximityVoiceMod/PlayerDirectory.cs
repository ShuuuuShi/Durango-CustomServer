using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProximityVoiceMod
{
    internal sealed class NearbyPlayer
    {
        public string EntityId;
        public string Name;
        public Vector3 Position;
    }

    /// <summary>
    /// อ่านผู้เล่นใกล้เคียงจาก PlayerManager ของเกม (Assembly-CSharp)
    /// ห่อไว้ที่เดียว เผื่ออัปเดตเกมแล้ว API เปลี่ยน
    /// </summary>
    internal static class PlayerDirectory
    {
        public static bool TryGetLocal(out string entityId, out string name, out Vector3 position)
        {
            entityId = "";
            name = "";
            position = Vector3.zero;
            try
            {
                PlayerBehavior local = PlayerBehavior.LocalPlayer;
                if (local == null) return false;
                entityId = local.EntityId ?? "";
                name = !string.IsNullOrEmpty(local.PlayerName) ? local.PlayerName : entityId;
                position = local.CurrentPosition;
                return !string.IsNullOrEmpty(entityId);
            }
            catch
            {
                return false;
            }
        }

        public static void GetNearby(List<NearbyPlayer> into)
        {
            if (into == null) return;
            into.Clear();
            try
            {
                PlayerManager pm = PlayerManager.Instance();
                if (pm == null) return;
                foreach (PlayerBehavior p in pm.GetPlayers())
                {
                    if (p == null || p.IsLocalPlayer) continue;
                    NearbyPlayer n = new NearbyPlayer();
                    n.EntityId = p.EntityId ?? "";
                    n.Name = !string.IsNullOrEmpty(p.PlayerName) ? p.PlayerName : n.EntityId;
                    n.Position = p.CurrentPosition;
                    if (!string.IsNullOrEmpty(n.EntityId)) into.Add(n);
                }
            }
            catch (Exception)
            {
                // ignore — voice still works for party/radio if positions missing
            }
        }

        public static string InferRoomId(string fallback)
        {
            try
            {
                if (!string.IsNullOrEmpty(GameManager.ClusterKey))
                    return GameManager.ClusterKey;
                if (GameManager.Region != null && !string.IsNullOrEmpty(GameManager.Region.Id))
                    return GameManager.Region.Id;
            }
            catch { }
            return string.IsNullOrEmpty(fallback) ? "default" : fallback;
        }
    }
}
