using UnityEngine;

namespace BorderRepair.TwoNight
{
    /// <summary>两晚切片的试算数字（整数信用点，可在资产里改）。本轮只用到第一晚：500 + 400 − 100 = 800；房租 1200，第三晚打烊前到期。</summary>
    [CreateAssetMenu(menuName = "Border Repair/Two-Night Economy", fileName = "TwoNightEconomy")]
    public class TwoNightEconomy : ScriptableObject
    {
        public int startingCash = 500;
        public int communicatorIncome = 400;
        public int communicatorPartsCost = 100;
        public int rent = 1200;
        public int rentDueNight = 3;

        /// <summary>没有资产时用的默认值（与上面相同）。</summary>
        public static TwoNightEconomy Defaults() => CreateInstance<TwoNightEconomy>();
    }
}
