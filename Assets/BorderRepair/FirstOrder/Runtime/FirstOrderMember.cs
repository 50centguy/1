using UnityEngine;

namespace BorderRepair.FirstOrder
{
    /// <summary>挂在总成成员网格上（例如进气唇口、护栅、风道），点到它等于点到总成主对象（上盖）。</summary>
    public class FirstOrderMember : MonoBehaviour
    {
        public FirstOrderPart owner;
    }
}
