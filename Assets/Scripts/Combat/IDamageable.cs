namespace SurvivalDemo.Combat
{
    /// <summary>
    /// 可受傷介面：所有可被傷害的物件（敵人、門、玩家）實作此介面。
    /// </summary>
    public interface IDamageable
    {
        void TakeDamage(float damage);
        bool IsAlive { get; }
    }
}
