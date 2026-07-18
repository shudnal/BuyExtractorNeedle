using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace BuyExtractorNeedle
{
    internal sealed class DvergrPaidItemStorage : MonoBehaviour
    {
        private static readonly int PaidItemsZdoKey = "shudnal.BuyExtractorNeedle.PaidItems".GetStableHashCode();
        private const string AcceptPurchaseRpc = "BEN_AcceptPurchase";
        private const string StorePaymentRpc = "BEN_StorePayment";
        private const string PlayResponseRpc = "BEN_PlayResponse";

        private NpcTalk npcTalk;
        internal ZNetView nview;
        private bool initialized;

        private void Awake()
        {
            Initialize();
        }

        private bool Initialize()
        {
            if (initialized)
                return true;

            Character character = GetComponent<Character>();
            npcTalk = GetComponent<NpcTalk>();
            nview = GetComponent<ZNetView>();

            if (character == null || npcTalk == null || nview == null || nview.GetZDO() == null)
                return false;

            nview.Register<ZPackage>(AcceptPurchaseRpc, RPC_AcceptPurchase);
            nview.Register<ZPackage>(StorePaymentRpc, RPC_StorePayment);
            nview.Register<int>(PlayResponseRpc, RPC_PlayResponse);
            initialized = true;
            return true;
        }

        internal bool AcceptPurchase(BuyAndDestroy.PurchaseData purchase)
        {
            if (!Initialize() || !nview.IsValid() || purchase == null)
                return false;

            nview.InvokeRPC(AcceptPurchaseRpc, BuyAndDestroy.CreatePurchasePackage(purchase.BuyerName, purchase.PurchasedPrefabName, purchase.Payment));
            return true;
        }

        private bool StorePayment(BuyAndDestroy.PurchaseData purchase)
        {
            if (!Initialize() || !nview.IsValid() || purchase == null)
                return false;

            nview.InvokeRPC(StorePaymentRpc, BuyAndDestroy.CreatePurchasePackage(purchase.BuyerName, purchase.PurchasedPrefabName, purchase.Payment));
            return true;
        }

        private void RPC_AcceptPurchase(long sender, ZPackage package)
        {
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
                return;

            BuyAndDestroy.PurchaseData purchase = BuyAndDestroy.ReadPurchasePackage(package);
            DistributeAndStorePayment(purchase, BuyAndDestroy.DvergrSearchRadius);

            int responseIndex = UnityEngine.Random.Range(0, BuyAndDestroy.s_randomBuy.Count);
            nview.InvokeRPC(ZRoutedRpc.Everybody, PlayResponseRpc, responseIndex);
        }

        private void RPC_StorePayment(long sender, ZPackage package)
        {
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
                return;

            BuyAndDestroy.PurchaseData purchase = BuyAndDestroy.ReadPurchasePackage(package);
            StoreItems(purchase.Payment, purchase);
        }

        private void RPC_PlayResponse(long sender, int lineIndex)
        {
            BuyAndDestroy.PlaySaleResponse(npcTalk, lineIndex);
        }

        private void DistributeAndStorePayment(BuyAndDestroy.PurchaseData purchase, float radius)
        {
            List<BuyAndDestroy.PaymentItem> mainPayment = ClonePayment(purchase.Payment);
            List<DvergrPaidItemStorage> neighbors = FindNearby(transform.position, radius, this);
            List<string> allocations = new List<string>();
            int nextNeighbor = 0;

            foreach (BuyAndDestroy.PaymentItem item in mainPayment)
            {
                int maxStackSize = GetMaxStackSize(item.PrefabName);
                while (item.Amount > maxStackSize && nextNeighbor < neighbors.Count)
                {
                    DvergrPaidItemStorage neighbor = neighbors[nextNeighbor++];
                    List<BuyAndDestroy.PaymentItem> stack = new List<BuyAndDestroy.PaymentItem>
                    {
                        item.Clone(maxStackSize)
                    };
                    BuyAndDestroy.PurchaseData neighborPurchase = new BuyAndDestroy.PurchaseData(purchase.BuyerName, purchase.PurchasedPrefabName, stack);

                    if (neighbor.StorePayment(neighborPurchase))
                    {
                        item.Amount -= maxStackSize;
                        allocations.Add(neighbor.GetLogDescription() + " <- " + BuyAndDestroy.FormatPayment(stack));
                    }
                    else if (BuyExtractorNeedle.instance != null)
                    {
                        BuyExtractorNeedle.LogWarning("Could not assign " + BuyAndDestroy.FormatPayment(stack) + " from player '" + purchase.BuyerName + "' to " + neighbor.GetLogDescription() + ". The main Dvergr will keep it instead.");
                    }
                }
            }

            StoreItems(mainPayment, purchase);
            allocations.Insert(0, GetLogDescription() + " <- " + BuyAndDestroy.FormatPayment(mainPayment));

            if (BuyExtractorNeedle.instance != null)
            {
                BuyExtractorNeedle.LogInfo("Payment distribution for player '" + purchase.BuyerName + "' buying '" + purchase.PurchasedPrefabName + "': " + string.Join("; ", allocations.ToArray()) + ".");
            }
        }

        private void StoreItems(IEnumerable<BuyAndDestroy.PaymentItem> payment, BuyAndDestroy.PurchaseData purchase)
        {
            ZDO zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null)
            {
                if (BuyExtractorNeedle.instance != null)
                    BuyExtractorNeedle.LogWarning("Could not store payment on " + GetLogDescription() + " because its ZDO is unavailable.");
                return;
            }

            List<BuyAndDestroy.PaymentItem> assignedPayment = ClonePayment(payment);
            Dictionary<string, int> storedItems = ReadStoredItems(zdo);
            foreach (BuyAndDestroy.PaymentItem item in assignedPayment)
            {
                storedItems.TryGetValue(item.PrefabName, out int currentAmount);
                storedItems[item.PrefabName] = (int)Math.Min(int.MaxValue, (long)currentAmount + item.Amount);
            }

            WriteStoredItems(zdo, storedItems);

            if (BuyExtractorNeedle.instance != null)
            {
                BuyExtractorNeedle.LogInfo("Stored payment from player '" + purchase.BuyerName + "' for '" + purchase.PurchasedPrefabName + "' on " + GetLogDescription() + ": added " + BuyAndDestroy.FormatPayment(assignedPayment) + "; total stored " + FormatStoredItems(storedItems) + ".");
            }
        }

        private static List<BuyAndDestroy.PaymentItem> ClonePayment(IEnumerable<BuyAndDestroy.PaymentItem> payment)
        {
            List<BuyAndDestroy.PaymentItem> result = new List<BuyAndDestroy.PaymentItem>();
            if (payment == null)
                return result;

            foreach (BuyAndDestroy.PaymentItem item in payment)
            {
                if (item != null && item.Amount > 0)
                    result.Add(item.Clone(item.Amount));
            }

            return result;
        }

        private static int GetMaxStackSize(string prefabName)
        {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
            ItemDrop itemDrop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            return itemDrop != null && itemDrop.m_itemData != null && itemDrop.m_itemData.m_shared != null
                ? Math.Max(1, itemDrop.m_itemData.m_shared.m_maxStackSize)
                : int.MaxValue;
        }

        internal static DvergrPaidItemStorage FindClosest(Vector3 position, float radius)
        {
            List<DvergrPaidItemStorage> nearby = FindNearby(position, radius, null);
            return nearby.Count > 0 ? nearby[0] : null;
        }

        private static List<DvergrPaidItemStorage> FindNearby(Vector3 position, float radius, DvergrPaidItemStorage excluded)
        {
            List<Character> characters = new List<Character>();
            Character.GetCharactersInRange(position, radius, characters);

            List<DvergrPaidItemStorage> result = new List<DvergrPaidItemStorage>();
            foreach (Character candidate in characters)
            {
                if (!IsDvergr(candidate))
                    continue;

                DvergrPaidItemStorage storage = candidate.GetComponent<DvergrPaidItemStorage>();
                if (storage == null)
                    storage = candidate.gameObject.AddComponent<DvergrPaidItemStorage>();

                if (storage == excluded || !storage.Initialize() || !storage.nview.IsValid())
                    continue;

                result.Add(storage);
            }

            result.Sort((left, right) =>
            {
                float leftDistance = (left.transform.position - position).sqrMagnitude;
                float rightDistance = (right.transform.position - position).sqrMagnitude;
                return leftDistance.CompareTo(rightDistance);
            });
            return result;
        }

        internal static bool IsDvergr(Character candidate)
        {
            if (candidate == null || candidate.GetComponent<NpcTalk>() == null)
                return false;

            ZNetView candidateView = candidate.GetComponent<ZNetView>();
            if (candidateView == null || candidateView.GetZDO() == null)
                return false;

            string prefabName = candidateView.GetPrefabName();
            return !string.IsNullOrEmpty(prefabName) && prefabName.StartsWith("Dverger", StringComparison.Ordinal);
        }

        internal string GetLogDescription()
        {
            string prefabName = nview != null ? nview.GetPrefabName() : gameObject.name;
            if (string.IsNullOrEmpty(prefabName))
                prefabName = gameObject.name;
            return "'" + prefabName + "' at " + transform.position.ToString("F1");
        }

        private static string GetLogDescription(Character character, ZNetView characterView)
        {
            string prefabName = characterView != null ? characterView.GetPrefabName() : character != null ? character.gameObject.name : "Unknown Dvergr";
            if (string.IsNullOrEmpty(prefabName) && character != null)
                prefabName = character.gameObject.name;
            Vector3 position = character != null ? character.transform.position : Vector3.zero;
            return "'" + prefabName + "' at " + position.ToString("F1");
        }

        private static Dictionary<string, int> ReadStoredItems(ZDO zdo)
        {
            Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.Ordinal);
            if (zdo == null)
                return result;

            string serialized = zdo.GetString(PaidItemsZdoKey, "");
            if (string.IsNullOrEmpty(serialized))
                return result;

            foreach (string entry in serialized.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = entry.LastIndexOf(':');
                if (separator <= 0 || separator >= entry.Length - 1)
                    continue;

                string prefabName = entry.Substring(0, separator);
                if (!int.TryParse(entry.Substring(separator + 1), out int amount) || amount <= 0)
                    continue;

                result.TryGetValue(prefabName, out int existing);
                result[prefabName] = (int)Math.Min(int.MaxValue, (long)existing + amount);
            }

            return result;
        }

        private static void WriteStoredItems(ZDO zdo, Dictionary<string, int> items)
        {
            if (zdo == null)
                return;

            List<string> entries = new List<string>();
            foreach (KeyValuePair<string, int> item in items)
            {
                if (!string.IsNullOrEmpty(item.Key) && item.Value > 0)
                    entries.Add(item.Key + ":" + item.Value);
            }

            entries.Sort(StringComparer.Ordinal);
            zdo.Set(PaidItemsZdoKey, string.Join(";", entries.ToArray()));
        }

        private static string FormatStoredItems(IDictionary<string, int> items)
        {
            if (items == null || items.Count == 0)
                return "nothing";

            return string.Join(", ", items
                .Where(item => !string.IsNullOrEmpty(item.Key) && item.Value > 0)
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => item.Key + " x" + item.Value)
                .ToArray());
        }

        private static void DropPaidItems(CharacterDrop characterDrop)
        {
            Character droppedCharacter = characterDrop != null ? characterDrop.GetComponent<Character>() : null;
            if (droppedCharacter == null && characterDrop != null)
                droppedCharacter = characterDrop.GetComponentInParent<Character>();
            if (droppedCharacter == null)
                return;

            ZNetView characterView = droppedCharacter.GetComponent<ZNetView>();
            ZDO zdo = characterView != null ? characterView.GetZDO() : null;
            string serialized = zdo != null ? zdo.GetString(PaidItemsZdoKey, "") : "";
            if (string.IsNullOrEmpty(serialized))
                return;

            string dvergrDescription = GetLogDescription(droppedCharacter, characterView);
            if (!IsDvergr(droppedCharacter))
            {
                if (BuyExtractorNeedle.instance != null)
                    BuyExtractorNeedle.LogWarning(dvergrDescription + " has stored payment but was not recognized as a Dvergr at death. Raw stored value: " + serialized + ".");
                return;
            }

            if (characterView == null || !characterView.IsValid())
            {
                if (BuyExtractorNeedle.instance != null)
                    BuyExtractorNeedle.LogWarning(dvergrDescription + " died with stored payment, but its ZNetView was not valid. Raw stored value: " + serialized + ".");
                return;
            }

            if (!characterView.IsOwner())
            {
                if (BuyExtractorNeedle.instance != null)
                    BuyExtractorNeedle.LogInfo("Stored payment drop for " + dvergrDescription + " was skipped on this peer because it is not the ZDO owner. Raw stored value: " + serialized + ".");
                return;
            }

            Dictionary<string, int> storedItems = ReadStoredItems(zdo);
            if (storedItems.Count == 0)
            {
                if (BuyExtractorNeedle.instance != null)
                    BuyExtractorNeedle.LogWarning(dvergrDescription + " died with an unreadable stored payment value: " + serialized + ".");
                return;
            }

            if (BuyExtractorNeedle.instance != null)
                BuyExtractorNeedle.LogInfo(dvergrDescription + " died with stored payment: " + FormatStoredItems(storedItems) + ".");

            Dictionary<string, int> droppedItems = new Dictionary<string, int>(StringComparer.Ordinal);
            int droppedStacks = 0;
            foreach (KeyValuePair<string, int> storedItem in storedItems)
            {
                GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(storedItem.Key) : null;
                ItemDrop itemDrop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (itemDrop == null || itemDrop.m_itemData == null || itemDrop.m_itemData.m_shared == null)
                {
                    if (BuyExtractorNeedle.instance != null)
                        BuyExtractorNeedle.LogWarning("Cannot drop stored payment from " + dvergrDescription + " because item prefab was not found: " + storedItem.Key + " x" + storedItem.Value + ".");
                    continue;
                }

                int maxStackSize = Math.Max(1, itemDrop.m_itemData.m_shared.m_maxStackSize);
                int remaining = storedItem.Value;
                while (remaining > 0)
                {
                    int stack = Math.Min(maxStackSize, remaining);
                    Vector3 dropPosition = droppedCharacter.transform.position + Vector3.up + UnityEngine.Random.insideUnitSphere * 0.4f;
                    ItemDrop droppedItem;
                    try
                    {
                        droppedItem = BuyAndDestroy.DropItemStack(prefab, itemDrop, stack, dropPosition, UnityEngine.Random.rotation);
                    }
                    catch (Exception exception)
                    {
                        if (BuyExtractorNeedle.instance != null)
                            BuyExtractorNeedle.LogWarning("Failed to drop " + storedItem.Key + " x" + stack + " from " + dvergrDescription + ": " + exception + ".");
                        break;
                    }

                    if (droppedItem == null)
                    {
                        if (BuyExtractorNeedle.instance != null)
                            BuyExtractorNeedle.LogWarning("Failed to create dropped item " + storedItem.Key + " x" + stack + " for " + dvergrDescription + ".");
                        break;
                    }

                    droppedItems.TryGetValue(storedItem.Key, out int alreadyDropped);
                    droppedItems[storedItem.Key] = alreadyDropped + stack;
                    droppedStacks++;
                    remaining -= stack;
                }
            }

            zdo.Set(PaidItemsZdoKey, "");

            if (BuyExtractorNeedle.instance == null)
                return;

            int expectedAmount = storedItems.Sum(item => item.Value);
            int droppedAmount = droppedItems.Sum(item => item.Value);
            if (droppedAmount == expectedAmount)
            {
                BuyExtractorNeedle.LogInfo(dvergrDescription + " dropped stored payment: " + FormatStoredItems(droppedItems) + " in " + droppedStacks + " stack(s).");
            }
            else
            {
                BuyExtractorNeedle.LogWarning(dvergrDescription + " dropped only " + FormatStoredItems(droppedItems) + " in " + droppedStacks + " stack(s); expected " + FormatStoredItems(storedItems) + ".");
            }
        }

        [HarmonyPatch(typeof(Character), "Awake")]
        private static class CharacterAwakePatch
        {
            private static void Postfix(Character __instance)
            {
                if (IsDvergr(__instance) && __instance.GetComponent<DvergrPaidItemStorage>() == null)
                    __instance.gameObject.AddComponent<DvergrPaidItemStorage>();
            }
        }

        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
        private static class CharacterDropGenerateDropListPatch
        {
            private static void Postfix(CharacterDrop __instance)
            {
                DropPaidItems(__instance);
            }
        }
    }
}
