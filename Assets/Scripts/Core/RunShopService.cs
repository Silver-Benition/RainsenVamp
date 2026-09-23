using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>一次商店报价；锁定时保存本次价格和品质，不受下一回合涨价影响。</summary>
public sealed class RunShopOffer
{
    public RunShopProduct Product { get; }
    public int Tier { get; }
    public int Price { get; }
    public bool Locked { get; internal set; }
    /// <summary>建立不可变报价。</summary>
    public RunShopOffer(RunShopProduct product, int tier, int price)
    { Product = product; Tier = tier; Price = price; }
}

/// <summary>局内交易服务；只在局间开放，所有校验在扣款前完成。</summary>
public sealed class RunShopService
{
    private readonly RunShopCatalogSO _catalog;
    private readonly RunMaterialWallet _wallet;
    private readonly LevelUpManager _loadout;
    private readonly AbilityManager _items;
    private readonly PlayerStats _stats;
    private readonly Func<bool> _canTrade;
    private readonly List<RunShopProduct> _eligible = new List<RunShopProduct>();
    private readonly RunShopOffer[] _offers = new RunShopOffer[4];
    private bool _busy;
    private int _wave;
    private int _rerolls;
    public bool IsBusy => _busy;
    public IReadOnlyList<RunShopOffer> Offers => _offers;
    /// <summary>全部报价为空时免费补货；存在任何商品（含锁定商品）时按本波付费刷新档位报价。</summary>
    public int RefreshPrice => IsEmpty ? 0 : (int)Math.Min(int.MaxValue, (long)_catalog.initialRerollPrice + _wave + (long)_rerolls * _catalog.rerollPriceStep);

    /// <summary>空店按四格报价的实际状态判断，购买或禁用清空均适用，无需额外标记。</summary>
    private bool IsEmpty
    {
        get
        {
            foreach (RunShopOffer offer in _offers) if (offer != null) return false;
            return true;
        }
    }

    /// <summary>注入本局依赖；外部决定当前阶段是否允许交易。</summary>
    public RunShopService(RunShopCatalogSO catalog, RunMaterialWallet wallet, LevelUpManager loadout,
        AbilityManager items, PlayerStats stats, Func<bool> canTrade)
    { _catalog = catalog; _wallet = wallet; _loadout = loadout; _items = items; _stats = stats; _canTrade = canTrade; }

    /// <summary>进入新商店，保留锁定报价并重置刷新次数。</summary>
    public void Enter(int completedWave)
    { if (_busy) return; _wave = completedWave; _rerolls = 0; FillUnlocked(); }

    /// <summary>按下一回合可出现档位和 Luck 抽取品质，早期保留基础装备。</summary>
    private int RollTier()
    {
        if (_stats != null && _stats.UsesBrotatoStats)
            return BrotatoStatRules.RollTier(_wave, _stats.GetFinalStat(PlayerStatType.LuckPoints), UnityEngine.Random.value);
        float luck = Mathf.Max(0.1f, _stats != null ? _stats.Luck : 1);
        float roll = UnityEngine.Random.value;
        if (_wave >= 8 && roll < Mathf.Min(0.15f, 0.02f * luck)) return 4;
        if (_wave >= 4 && roll < Mathf.Min(0.35f, 0.10f * luck)) return 3;
        return _wave >= 2 && roll < Mathf.Min(0.7f, 0.3f * luck) ? 2 : 1;
    }

    /// <summary>过滤封印、本局放逐道具、已满道具与锁定内容；候选不足时保留空位。</summary>
    private void FillUnlocked()
    {
        _eligible.Clear();
        // 宝箱阶段也能禁用道具；进入商店时连同上波锁定报价一起清除。
        for (int i = 0; i < _offers.Length; i++)
            if (_offers[i] != null && !_offers[i].Product.IsWeapon && (RunState.GetOrCreate(_stats).IsBanished(_offers[i].Product.Id) ||
                (_stats != null && _stats.UsesBrotatoStats && !_offers[i].Product.content.abilityToGrant.IsAvailableInBrotato())))
                _offers[i] = null;
        foreach (RunShopProduct product in _catalog.products)
        {
            if (_stats != null && _stats.UsesBrotatoStats && !product.IsWeapon &&
                !product.content.abilityToGrant.IsAvailableInBrotato()) continue;
            if (AccountProgressService.Current.IsUpgradeSealed(product.Id)) continue;
            if (!product.IsWeapon && RunState.GetOrCreate(_stats).IsBanished(product.Id)) continue;
            bool locked = false;
            foreach (RunShopOffer offer in _offers)
                if (offer != null && offer.Locked && offer.Product.Id == product.Id) locked = true;
            if (locked || (!product.IsWeapon && !CanGrantItem(product))) continue;
            _eligible.Add(product);
        }
        for (int i = 0; i < _offers.Length; i++)
        {
            if (_offers[i] != null && _offers[i].Locked) continue;
            _offers[i] = null;
            if (_eligible.Count == 0) continue;
            int index = UnityEngine.Random.Range(0, _eligible.Count);
            // 前两回合先给出武器选择，避免唯一开局武器后长期没有装备成长。
            if (_wave <= 2 && i < 2)
                for (int j = 0; j < _eligible.Count; j++) if (_eligible[j].IsWeapon) { index = j; break; }
            RunShopProduct product = _eligible[index];
            int tier = product.IsWeapon ? RollTier() : 1;
            int price = (int)Math.Min(int.MaxValue, (long)product.basePrice * tier + _wave * 2L);
            _offers[i] = new RunShopOffer(product, tier, price);
            _eligible.RemoveAt(index);
        }
    }

    /// <summary>检查道具的独立配置上限；回合模式不采用六种能力容量。</summary>
    private bool CanGrantItem(RunShopProduct product)
    {
        if (_items == null || product.content.abilityToGrant == null) return false;
        if (_stats != null && _stats.UsesBrotatoStats && !product.content.abilityToGrant.IsAvailableInBrotato()) return false;
        OwnedAbilityState state = _items.GetOwnedAbility(product.content.abilityToGrant);
        return state == null || state.CurrentLevel < state.Data.MaxLevel;
    }

    /// <summary>校验钱包、槽位和报价后授予一次商品；同步事件重入被阻止。</summary>
    public bool Buy(int slot)
    {
        if (_busy || !_canTrade() || slot < 0 || slot >= _offers.Length) return false;
        RunShopOffer offer = _offers[slot];
        if (offer == null || _wallet.Balance < offer.Price) return false;
        if (offer.Product.IsWeapon ? !_loadout.CanBuyRoundWeapon(offer.Product.content.weaponToGrant, offer.Tier)
            : !CanGrantItem(offer.Product)) return false;
        _busy = true;
        try
        {
            return _wallet.Transact(offer.Price, 0, () =>
            {
                // 先移除报价与预留余额；装备事件观察者只能读到这笔交易后的状态。
                _offers[slot] = null;
                bool granted = offer.Product.IsWeapon
                    ? _loadout.BuyRoundWeapon(offer.Product.content.weaponToGrant, offer.Tier) != null
                    : _items.GrantOrUpgrade(offer.Product.content.abilityToGrant) != null;
                if (!granted) _offers[slot] = offer;
                return granted;
            });
        }
        finally { _busy = false; }
    }

    /// <summary>只允许在商店放逐道具；消费次数、登记排除和清空同 ID 报价在发布事件前完成。</summary>
    public bool Banish(int slot)
    {
        if (_busy || !_canTrade() || slot < 0 || slot >= _offers.Length) return false;
        RunShopOffer offer = _offers[slot];
        if (offer == null || offer.Product.IsWeapon) return false;
        RunState state = RunState.GetOrCreate(_stats);
        if (state.RemainingBanishes <= 0 || state.IsBanished(offer.Product.Id)) return false;
        _busy = true;
        try
        {
            return _wallet.Transact(0, 0, () =>
            {
                if (!state.TryBanishUpgrade(offer.Product.Id)) return false;
                // 清除所有同 ID 道具报价；锁定不能绕过本局排除。
                for (int i = 0; i < _offers.Length; i++)
                    if (_offers[i] != null && !_offers[i].Product.IsWeapon && _offers[i].Product.Id == offer.Product.Id)
                        _offers[i] = null;
                return true;
            });
        }
        finally { _busy = false; }
    }

    /// <summary>免费切换报价锁；空位和非商店阶段不产生变化。</summary>
    public bool ToggleLock(int slot)
    {
        if (_busy || !_canTrade() || slot < 0 || slot >= 4 || _offers[slot] == null) return false;
        _offers[slot].Locked = !_offers[slot].Locked;
        return true;
    }

    /// <summary>先确认存在可刷新位置，再扣款刷新；全锁定不收费。</summary>
    public bool Refresh()
    {
        if (_busy || !_canTrade()) return false;
        bool hasSlot = false;
        foreach (RunShopOffer offer in _offers) if (offer == null || !offer.Locked) hasSlot = true;
        if (!hasSlot || _wallet.Balance < RefreshPrice) return false;
        _busy = true;
        try
        {
            bool freeRestock = IsEmpty;
            int price = RefreshPrice;
            return _wallet.Transact(price, 0, () =>
            { FillUnlocked(); if (!freeRestock) _rerolls++; return true; });
        }
        finally { _busy = false; }
    }

    /// <summary>回收指定武器实例；价值由当前品质和目录配置确定，不发经验。</summary>
    public bool Recycle(WeaponBase weapon)
    {
        if (_busy || !_canTrade() || weapon == null) return false;
        RunShopProduct product = _catalog.products.Find(p => p.IsWeapon && p.content.weaponToGrant == weapon.weaponData);
        if (product == null) return false;
        int amount = Mathf.FloorToInt((product.basePrice * weapon.CurrentLevel + _wave * 2) * _catalog.recycleRatio);
        _busy = true;
        try
        {
            return _wallet.Transact(0, amount, () => _loadout.RemoveRoundWeapon(weapon));
        }
        finally { _busy = false; }
    }

    /// <summary>手动合并只消耗两把同种同品质武器，不消费材料。</summary>
    public bool Combine(WeaponBase weapon)
    {
        if (_busy || !_canTrade()) return false;
        _busy = true;
        try { return _loadout.CombineRoundWeapon(weapon); }
        finally { _busy = false; }
    }

}
