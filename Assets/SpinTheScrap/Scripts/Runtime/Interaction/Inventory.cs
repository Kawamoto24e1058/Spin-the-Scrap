using System.Collections.Generic;
using SpinTheScrap.Core;
using UnityEngine;

namespace SpinTheScrap.Interaction
{
    /// <summary>
    /// 小さいガラクタを入れるインベントリ枠。入れたガラクタは非アクティブにして持ち運ぶ。
    /// 将来は道具（懐中電灯・スキャナーなど）も枠を消費する（要件書 4.2）。
    /// </summary>
    public sealed class Inventory : MonoBehaviour
    {
        [SerializeField] int slotCount = 4;

        Scrap[] slots;

        public int SlotCount => slotCount;
        public int SelectedIndex { get; private set; }
        public Scrap Selected => slots[SelectedIndex];

        public float TotalMass
        {
            get
            {
                float total = 0f;
                foreach (var s in slots)
                    if (s != null) total += s.Body.mass;
                return total;
            }
        }

        public IEnumerable<Scrap> Items
        {
            get
            {
                foreach (var s in slots)
                    if (s != null) yield return s;
            }
        }

        public Scrap this[int index] => slots[index];

        void Awake()
        {
            slots = new Scrap[slotCount];
        }

        public void Select(int index)
        {
            if (index >= 0 && index < slotCount) SelectedIndex = index;
        }

        public bool TryAdd(Scrap scrap)
        {
            if (scrap.Size != ScrapSize.Small) return false;
            int index = slots[SelectedIndex] == null ? SelectedIndex : System.Array.IndexOf(slots, null);
            if (index < 0) return false;
            slots[index] = scrap;
            scrap.gameObject.SetActive(false);
            return true;
        }

        /// <summary>選択中の枠から取り出す。ワールドへの再配置は呼び出し側が行う。</summary>
        public Scrap TakeSelected()
        {
            var scrap = slots[SelectedIndex];
            slots[SelectedIndex] = null;
            return scrap;
        }

        /// <summary>中身をすべて取り出す（脱出時の回収など）。</summary>
        public List<Scrap> TakeAll()
        {
            var list = new List<Scrap>(Items);
            for (int i = 0; i < slots.Length; i++) slots[i] = null;
            return list;
        }
    }
}
