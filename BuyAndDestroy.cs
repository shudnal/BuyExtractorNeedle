using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BuyExtractorNeedle
{
    public class BuyAndDestroy : MonoBehaviour, Hoverable, Interactable
    {
        internal const float DvergrSearchRadius = 30f;
        private const string PurchaseRpc = "BEN_BuyAndRemove";
        private static readonly int PurchaseProcessedZdoKey = "shudnal.BuyExtractorNeedle.PurchaseProcessed".GetStableHashCode();

        internal sealed class PaymentItem
        {
            internal readonly string PrefabName;
            internal readonly string SharedName;
            internal int Amount;

            internal PaymentItem(string prefabName, string sharedName, int amount)
            {
                PrefabName = prefabName;
                SharedName = sharedName;
                Amount = amount;
            }

            internal PaymentItem Clone(int amount)
            {
                return new PaymentItem(PrefabName, SharedName, amount);
            }
        }

        internal sealed class PurchaseData
        {
            internal readonly string BuyerName;
            internal readonly string PurchasedPrefabName;
            internal readonly List<PaymentItem> Payment;

            internal PurchaseData(string buyerName, string purchasedPrefabName, List<PaymentItem> payment)
            {
                BuyerName = buyerName;
                PurchasedPrefabName = purchasedPrefabName;
                Payment = payment ?? new List<PaymentItem>();
            }
        }

        public string m_text = "";
        public Destructible m_destructible;
        public WearNTear m_wnt;
        public ZNetView m_nview;
        public string m_requirements;
        public string m_description;
        public int m_myIndex = -1;

        public static readonly List<BuyAndDestroy> s_allInstances = new List<BuyAndDestroy>();

        public static readonly List<string> s_randomBuy = new List<string>
        {
            "$npc_haldor_random_buy1",
            "$npc_haldor_random_buy3",
            "$npc_haldor_random_buy4",
            "$npc_haldor_random_buy5",
            "$npc_haldor_random_sell1",
            "$npc_haldor_random_sell2",
            "$npc_haldor_random_greet4",
            "$npc_haldor_random_sell4"
        };

        public static EffectList s_randomSellFX;

        public void Awake()
        {
            EnsureSellEffects();

            IDestructible destructible = GetComponent<IDestructible>();
            m_destructible = destructible as Destructible;
            m_wnt = destructible as WearNTear;

            s_allInstances.Add(this);
            m_myIndex = s_allInstances.Count - 1;

            m_requirements = BuyExtractorNeedle.requirements.Value;
            m_nview = m_destructible != null ? m_destructible.m_nview : m_wnt != null ? m_wnt.m_nview : null;

            if (m_nview != null && m_nview.GetZDO() != null)
                m_nview.Register<ZPackage>(PurchaseRpc, RPC_BuyAndRemove);
        }

        public void OnDestroy()
        {
            if (m_myIndex < 0 || m_myIndex >= s_allInstances.Count)
                return;

            int lastIndex = s_allInstances.Count - 1;
            s_allInstances[m_myIndex] = s_allInstances[lastIndex];
            s_allInstances[m_myIndex].m_myIndex = m_myIndex;
            s_allInstances.RemoveAt(lastIndex);
            m_myIndex = -1;
        }

        public string GetHoverText()
        {
            if (m_destructible == null && m_wnt == null)
                return GetHoverName();

            if (string.IsNullOrWhiteSpace(m_description) && !string.IsNullOrWhiteSpace(m_requirements))
                m_description = GetDescription(m_requirements);

            string action = string.IsNullOrWhiteSpace(m_description) ? "$piece_itemstand_take" : "$store_buy " + m_description;
            return Localization.instance.Localize(m_text + "\n[<color=yellow><b>$KEY_Use</b></color>] " + action);
        }

        public string GetHoverName()
        {
            return Localization.instance.Localize(m_text);
        }

        public float GetHoverOffset()
        {
            return 0f;
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || alt || (m_destructible == null && m_wnt == null) || user == null)
                return false;

            if (m_nview == null || !m_nview.IsValid())
                return false;

            string buyerName = GetBuyerName(user);
            List<PaymentItem> payment = GetRequirements(m_requirements);
            if (!HasNearbyDvergr(transform.position))
            {
                Remove(Array.Empty<PaymentItem>(), buyerName);
                return true;
            }

            Inventory inventory = user.GetInventory();
            int pocketCoins = 0;
            if (payment.Count > 0 && !TryGetPocketCoins(user, out pocketCoins))
                return false;

            if (payment.Count > 0 && payment.Any(item => (long)inventory.CountItems(item.SharedName)
                + (item.SharedName == "$item_coins" ? pocketCoins : 0) < item.Amount))
            {
                PrivateArea.OnObjectDamaged(transform.position, user, false);
                return true;
            }

            // CurrencyPocket's postfix pays the remainder after inventory stacks are removed.
            foreach (PaymentItem item in payment)
                inventory.RemoveItem(item.SharedName, item.Amount);

            if (payment.Count > 0 && StoreGui.instance != null)
                StoreGui.instance.m_sellEffects.Create(transform.position, Quaternion.identity);

            Remove(payment, buyerName);
            return true;
        }

        private static bool TryGetPocketCoins(Humanoid user, out int coins)
        {
            coins = 0;
            if (!BuyExtractorNeedle.currencyPocketInstalled || user != Player.m_localPlayer
                || !Player.m_localPlayer.m_customData.TryGetValue("CoinPocket_CoinCount", out string value))
                return true;

            // CurrencyPocket parses this value even when RemoveItem is called for a non-coin item.
            // Validate it before any payment is removed, not just before spending pocket coins.
            if (int.TryParse(value, out coins) && coins >= 0)
                return true;

            BuyExtractorNeedle.LogWarning("Purchase cancelled because the CurrencyPocket balance is invalid. No payment was taken.");
            return false;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            return false;
        }

        private void Remove(IEnumerable<PaymentItem> payment, string buyerName)
        {
            if (m_nview == null || !m_nview.IsValid())
                return;

            string purchasedPrefabName = m_nview.GetPrefabName();
            m_nview.InvokeRPC(PurchaseRpc, CreatePurchasePackage(buyerName, purchasedPrefabName, payment));
        }

        private void RPC_BuyAndRemove(long sender, ZPackage package)
        {
            if (m_nview == null || !m_nview.IsValid() || !m_nview.IsOwner())
                return;

            PurchaseData purchase = ReadPurchasePackage(package);
            List<PaymentItem> payment = purchase.Payment;
            ZDO zdo = m_nview.GetZDO();
            if (zdo == null)
                return;

            if (zdo.GetBool(PurchaseProcessedZdoKey, false))
            {
                RefundPayment(payment);
                if (BuyExtractorNeedle.instance != null)
                    BuyExtractorNeedle.LogWarning("A repeated purchase of '" + purchase.PurchasedPrefabName + "' by player '" + purchase.BuyerName + "' was rejected and its payment was refunded: " + FormatPayment(payment) + ".");
                return;
            }

            zdo.Set(PurchaseProcessedZdoKey, true);

            DvergrPaidItemStorage storage = DvergrPaidItemStorage.FindClosest(transform.position, DvergrSearchRadius);
            if (storage == null)
            {
                RefundPayment(payment);
                if (BuyExtractorNeedle.instance != null)
                    BuyExtractorNeedle.LogInfo("Player '" + purchase.BuyerName + "' obtained '" + purchase.PurchasedPrefabName + "' for free because no Dvergr was available to accept payment.");
                DestroyPurchasedObject();
                return;
            }

            if (!storage.AcceptPurchase(purchase))
            {
                RefundPayment(payment);
                if (BuyExtractorNeedle.instance != null)
                    BuyExtractorNeedle.LogWarning("Purchase of '" + purchase.PurchasedPrefabName + "' by player '" + purchase.BuyerName + "' could not be assigned to " + storage.GetLogDescription() + ". Refunded payment: " + FormatPayment(payment) + ".");
                DestroyPurchasedObject();
                return;
            }

            if (BuyExtractorNeedle.instance != null)
                BuyExtractorNeedle.LogInfo("Player '" + purchase.BuyerName + "' bought '" + purchase.PurchasedPrefabName + "' for " + FormatPayment(payment) + ". Primary payment recipient: " + storage.GetLogDescription() + ".");

            DestroyPurchasedObject();
        }

        private void DestroyPurchasedObject()
        {
            if (m_destructible != null)
                m_destructible.Destroy();
            else if (m_wnt != null)
                m_wnt.Destroy();
        }

        private void RefundPayment(IEnumerable<PaymentItem> payment)
        {
            foreach (PaymentItem item in payment)
            {
                GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(item.PrefabName) : null;
                ItemDrop itemDrop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (itemDrop == null || itemDrop.m_itemData == null || itemDrop.m_itemData.m_shared == null)
                    continue;

                int maxStackSize = Math.Max(1, itemDrop.m_itemData.m_shared.m_maxStackSize);
                int remaining = item.Amount;
                while (remaining > 0)
                {
                    int stack = Math.Min(maxStackSize, remaining);
                    DropItemStack(prefab, itemDrop, stack, transform.position + Vector3.up, Quaternion.identity);
                    remaining -= stack;
                }
            }
        }

        internal static ItemDrop DropItemStack(GameObject prefab, ItemDrop itemDrop, int amount, Vector3 position, Quaternion rotation)
        {
            if (prefab == null || itemDrop == null || itemDrop.m_itemData == null || amount <= 0)
                return null;

            ItemDrop.ItemData itemData = itemDrop.m_itemData.Clone();
            itemData.m_dropPrefab = prefab;
            return ItemDrop.DropItem(itemData, amount, position, rotation);
        }

        internal static void PlaySaleResponse(NpcTalk npcTalk, int lineIndex)
        {
            if (npcTalk == null || s_randomBuy.Count == 0)
                return;

            EnsureSellEffects();
            s_randomSellFX.Create(npcTalk.transform.position, Quaternion.identity);

            int safeIndex = Mathf.Clamp(lineIndex, 0, s_randomBuy.Count - 1);
            npcTalk.Say(s_randomBuy[safeIndex], "Talk");
        }

        private static void EnsureSellEffects()
        {
            if (s_randomSellFX != null)
                return;

            Trader trader = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Haldor")?.GetComponent<Trader>() : null;
            s_randomSellFX = trader != null ? trader.m_randomSellFX : new EffectList();
        }

        private static bool HasNearbyDvergr(Vector3 position)
        {
            return DvergrPaidItemStorage.FindClosest(position, DvergrSearchRadius) != null;
        }

        private static string GetBuyerName(Humanoid user)
        {
            Player player = user as Player;
            if (player != null)
            {
                string playerName = player.GetPlayerName();
                if (!string.IsNullOrWhiteSpace(playerName))
                    return playerName;
            }

            return !string.IsNullOrWhiteSpace(user.name) ? user.name : "Unknown player";
        }

        private static string GetDescription(string requirements)
        {
            return string.Join(", ", GetRequirements(requirements).Select(item => item.SharedName + " x" + item.Amount).ToArray());
        }

        private static List<PaymentItem> GetRequirements(string requirements)
        {
            List<PaymentItem> result = new List<PaymentItem>();
            Dictionary<string, PaymentItem> items = new Dictionary<string, PaymentItem>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(requirements) || ObjectDB.instance == null)
                return result;

            foreach (string requirement in requirements.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = requirement.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                    continue;

                if (!int.TryParse(parts[1].Trim(), out int amount) || amount <= 0)
                    continue;

                string prefabName = parts[0].Trim();
                GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabName);
                ItemDrop itemDrop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                string sharedName = itemDrop != null && itemDrop.m_itemData != null && itemDrop.m_itemData.m_shared != null
                    ? itemDrop.m_itemData.m_shared.m_name
                    : null;

                if (string.IsNullOrEmpty(sharedName))
                    continue;

                if (items.TryGetValue(prefabName, out PaymentItem existing))
                {
                    existing.Amount = (int)Math.Min(int.MaxValue, (long)existing.Amount + amount);
                }
                else
                {
                    PaymentItem item = new PaymentItem(prefabName, sharedName, amount);
                    items.Add(prefabName, item);
                    result.Add(item);
                }
            }

            return result;
        }

        internal static ZPackage CreatePurchasePackage(string buyerName, string purchasedPrefabName, IEnumerable<PaymentItem> payment)
        {
            ZPackage package = new ZPackage();
            package.Write(buyerName ?? "Unknown player");
            package.Write(purchasedPrefabName ?? "Unknown prefab");
            WritePayment(package, payment);
            return package;
        }

        internal static PurchaseData ReadPurchasePackage(ZPackage package)
        {
            if (package == null)
                return new PurchaseData("Unknown player", "Unknown prefab", new List<PaymentItem>());

            try
            {
                string buyerName = package.ReadString();
                string purchasedPrefabName = package.ReadString();
                return new PurchaseData(buyerName, purchasedPrefabName, ReadPayment(package));
            }
            catch (Exception exception)
            {
                if (BuyExtractorNeedle.instance != null)
                    BuyExtractorNeedle.LogWarning("Failed to read purchase package: " + exception.Message);
                return new PurchaseData("Unknown player", "Unknown prefab", new List<PaymentItem>());
            }
        }

        internal static string FormatPayment(IEnumerable<PaymentItem> payment)
        {
            if (payment == null)
                return "nothing";

            string[] entries = payment
                .Where(item => item != null && item.Amount > 0)
                .Select(item => item.PrefabName + " x" + item.Amount)
                .ToArray();
            return entries.Length > 0 ? string.Join(", ", entries) : "nothing";
        }

        private static void WritePayment(ZPackage package, IEnumerable<PaymentItem> payment)
        {
            List<PaymentItem> items = payment != null ? payment.Where(item => item != null && item.Amount > 0).ToList() : new List<PaymentItem>();
            package.Write(items.Count);
            foreach (PaymentItem item in items)
            {
                package.Write(item.PrefabName);
                package.Write(item.Amount);
            }
        }

        private static List<PaymentItem> ReadPayment(ZPackage package)
        {
            List<PaymentItem> payment = new List<PaymentItem>();
            int count = Mathf.Clamp(package.ReadInt(), 0, 64);
            for (int i = 0; i < count; ++i)
            {
                string prefabName = package.ReadString();
                int amount = package.ReadInt();
                GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
                ItemDrop itemDrop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                string sharedName = itemDrop != null && itemDrop.m_itemData != null && itemDrop.m_itemData.m_shared != null
                    ? itemDrop.m_itemData.m_shared.m_name
                    : null;
                if (!string.IsNullOrEmpty(prefabName) && !string.IsNullOrEmpty(sharedName) && amount > 0)
                    payment.Add(new PaymentItem(prefabName, sharedName, amount));
            }

            return payment;
        }

        public static List<BuyAndDestroy> GetAllInstances()
        {
            return s_allInstances;
        }

        public static void UpdateDescription()
        {
            foreach (BuyAndDestroy item in s_allInstances)
            {
                item.m_requirements = BuyExtractorNeedle.requirements.Value;
                item.m_description = "";
            }
        }
    }
}
