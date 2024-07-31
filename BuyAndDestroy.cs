using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BuyExtractorNeedle
{
    public class BuyAndDestroy : MonoBehaviour, Hoverable, Interactable
    {
        public string m_text = "";

        public Destructible m_destructible;

        public WearNTear m_wnt;

        public string m_requirements;

        public string m_description;

        public int m_myIndex = -1;

        public static readonly List<BuyAndDestroy> s_allInstances = new List<BuyAndDestroy>();

        public static readonly List<string> s_randomBuy = new List<string>()
        {
            "$npc_haldor_random_buy1",
            "$npc_haldor_random_buy3",
            "$npc_haldor_random_buy4",
            "$npc_haldor_random_buy5",
            "$npc_haldor_random_sell1",
            "$npc_haldor_random_sell2"
        };

        public static EffectList s_randomSellFX;

        public void Awake()
        {
            if (s_randomSellFX == null && ZNetScene.instance)
                s_randomSellFX = ZNetScene.instance.GetPrefab("Haldor")?.GetComponent<Trader>()?.m_randomSellFX ?? new EffectList();

            IDestructible component = GetComponent<IDestructible>();
            
            m_destructible = component as Destructible;
            m_wnt = component as WearNTear;

            s_allInstances.Add(this);
            m_myIndex = s_allInstances.Count - 1;

            m_requirements = BuyExtractorNeedle.requirements.Value;
        }

        public void OnDestroy()
        {
            if (m_myIndex != -1)
            {
                s_allInstances[m_myIndex] = s_allInstances[s_allInstances.Count - 1];
                s_allInstances[m_myIndex].m_myIndex = m_myIndex;
                s_allInstances.RemoveAt(s_allInstances.Count - 1);
            }
        }

        public string GetHoverText()
        {
            if (!m_destructible && !m_wnt)
                return GetHoverName();

            if (string.IsNullOrWhiteSpace(m_description) && !string.IsNullOrWhiteSpace(m_requirements))
                m_description = GetDescription(m_requirements);

            return Localization.instance.Localize($"{m_text}\n[<color=yellow><b>$KEY_Use</b></color>] {(string.IsNullOrWhiteSpace(m_description) ? "$piece_itemstand_take" : $"$store_buy {m_description}")}");
        }

        public string GetHoverName()
        {
            return Localization.instance.Localize(m_text);
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold)
                return false;

            if (alt)
                return false;

            if (!m_destructible && !m_wnt)
                return false;

            List<Tuple<string, int>> requirements = GetRequirements(m_requirements);
            if (requirements.Count == 0 || requirements.All(req => user.GetInventory().CountItems(req.Item1) >= req.Item2))
            {
                requirements.ForEach(req => user.GetInventory().RemoveItem(req.Item1, req.Item2));
                if (m_destructible)
                    m_destructible.Destroy();
                if (m_wnt)
                    m_wnt.Destroy();

                if (requirements.Count > 0)
                    StoreGui.instance.m_sellEffects.Create(base.transform.position, Quaternion.identity);

                List<Character> characters = new List<Character>();
                Character.GetCharactersInRange(base.transform.position, 15f, characters);

                Character npc = characters.Where(c => c.GetComponent<NpcTalk>()).OrderBy(c => Vector3.Distance(c.transform.position, user.transform.position)).FirstOrDefault();
                if (npc != null)
                {
                    npc.GetComponent<NpcTalk>().Say(s_randomBuy[UnityEngine.Random.Range(0, s_randomBuy.Count)], "Talk");
                    s_randomSellFX?.Create(npc.transform.position, Quaternion.identity);
                }
            }
            else
            {
                PrivateArea.OnObjectDamaged(base.transform.position, attacker: user, destroyed: false);
            }

            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            return false;
        }

        private static string GetDescription(string requirements)
        {
            return string.Join(", ", GetRequirements(requirements).Select(req => $"{req.Item1} x{req.Item2}"));
        }

        private static List<Tuple<string, int>> GetRequirements(string requirements)
        {
            List<Tuple<string, int>> list = new List<Tuple<string, int>>();
            foreach (string requirement in requirements.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] req = requirement.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                if (req.Length != 2)
                    continue;

                int amount = int.Parse(req[1]);
                if (amount <= 0)
                    continue;

                GameObject prefab = ObjectDB.instance.GetItemPrefab(req[0].Trim());
                if (prefab == null)
                    continue;

                list.Add(Tuple.Create(prefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_name, amount));
            };

            return list;
        }

        public static List<BuyAndDestroy> GetAllInstances()
        {
            return s_allInstances;
        }

        public static void UpdateDescription()
        {
            foreach (var item in s_allInstances)
            {
                item.m_requirements = BuyExtractorNeedle.requirements.Value;
                item.m_description = "";
            }
        }
    }
}
