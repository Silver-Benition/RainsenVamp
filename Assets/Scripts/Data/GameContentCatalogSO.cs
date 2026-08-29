using System.Collections.Generic;
using UnityEngine;

/// <summary>集中登记收藏页面使用的角色、武器与升级内容。</summary>
[CreateAssetMenu(fileName = "GameContentCatalog", menuName = "GameData/Content Catalog")]
public sealed class GameContentCatalogSO : ScriptableObject
{
    [SerializeField] private List<CharacterDataSO> characters = new List<CharacterDataSO>();
    [SerializeField] private List<WeaponDataSO> weapons = new List<WeaponDataSO>();
    [SerializeField] private List<UpgradeDataSO> upgrades = new List<UpgradeDataSO>();

    /// <summary>收藏与解锁系统登记的角色列表。</summary>
    public IReadOnlyList<CharacterDataSO> Characters => characters;

    /// <summary>收藏系统登记的武器列表。</summary>
    public IReadOnlyList<WeaponDataSO> Weapons => weapons;

    /// <summary>收藏与 Seal 系统登记的升级项目列表。</summary>
    public IReadOnlyList<UpgradeDataSO> Upgrades => upgrades;
}
