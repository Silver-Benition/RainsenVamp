using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>正式 Prefab 和场景中的攻击行为回归，包含实际物理命中与图形证据。</summary>
    public sealed class EngineeringExpansionPlayModeTests
    {
        private object _round, _loadout, _pool;
        private Component _player;
        private readonly List<GameObject> _fixtures = new List<GameObject>();
        private readonly Dictionary<string, object> _data = new Dictionary<string, object>();
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>打开正式场景、使用内存账号，关闭自然刷怪并保留战斗阶段。</summary>
        [UnitySetUp] public IEnumerator Setup()
        {
            T("AccountProgressService").GetMethod("SetStorageForTests").Invoke(null,new[]{Activator.CreateInstance(T("InMemoryAccountProgressStorage"))});
            yield return SceneManager.LoadSceneAsync("MainLevel");
            for(int i=0;i<6;i++) yield return null;
            _round=UnityEngine.Object.FindObjectOfType(T("RoundController")); _loadout=Property(_round,"Loadout");
            _player=(Component)Property(_round,"Player"); _pool=UnityEngine.Object.FindObjectOfType(T("PoolManager"));
            foreach(Behaviour wave in UnityEngine.Object.FindObjectsOfType(T("WorldWaveManager"))) wave.enabled=false;
            foreach(Component enemy in UnityEngine.Object.FindObjectsOfType(T("EnemyBase"))) enemy.gameObject.SetActive(false);
            _data.Clear();
            foreach(object product in (IEnumerable)Field(Field(Field(_round,"config"),"shopCatalog"),"products"))
            { object data=Field(Field(product,"content"),"weaponToGrant"); if(data!=null) _data[(string)Field(data,"weaponID")]=data; }
            var owned=(IList)Property(_loadout,"OwnedWeapons");
            while(owned.Count>0) Call(_loadout,"RemoveRoundWeapon",owned[0]);
            // 移除装备不会召回已飞出的火球；清理开场攻击，避免它们干扰单发伤害断言。
            foreach(string type in new[]{"ProjectileBase","LobbedProjectile","MeleeSwingHitbox","AuraDamageZone","OrbitingProjectile"})
            foreach(Component attack in UnityEngine.Object.FindObjectsOfType(T(type)))
            {
                object data=Field(attack,type=="ProjectileBase"||type=="AuraDamageZone"||type=="ProjectileBase"?"weaponData":"_weaponData");
                if(data!=null) Call(_pool,"Release",Field(data,"projectilePrefab"),attack.gameObject);
            }
            yield return new WaitForSeconds(.5f);
        }

        /// <summary>卸载测试场景与独立夹具，恢复时间。</summary>
        [UnityTearDown] public IEnumerator Cleanup()
        {
            Time.timeScale=1;
            foreach(var fixture in _fixtures) if(fixture!=null) UnityEngine.Object.Destroy(fixture);
            _fixtures.Clear();
            Scene empty=SceneManager.CreateScene("EngineeringExpansionEmpty"); SceneManager.SetActiveScene(empty);
            Scene main=SceneManager.GetSceneByName("MainLevel"); if(main.isLoaded) yield return SceneManager.UnloadSceneAsync(main);
        }

        /// <summary>模拟长帧跨过灼烧到期与三次墨带跳伤，并验证拳套排除表不误伤池复用新生命。</summary>
        [UnityTest] public IEnumerator LongFrameAndEnemyReuse_PreserveScheduledTicksAndGeneration()
        {
            Vector3 p=new Vector3(4,0);Component victim=Enemy(p+Vector3.right);Physics2D.SyncTransforms();object status=Property(victim,"CombatStatus");
            Call(status,"ApplyBurn",_player.transform,4f,null);IList burns=(IList)Field(status,"_burns");object burn=burns[0];Set(burn,"Until",Time.time-.01f);burns[0]=burn;Set(status,"_nextBurn",Time.time-1.01f);Call(status,"Update");Assert.AreEqual(88f,Property(victim,"CurrentHealth"));
            Component ink=Effect("Ink",p,5,3.5f,.65f);Set(ink,"_time",1.6f);Call(ink,"Ink");Assert.AreEqual(73f,Property(victim,"CurrentHealth"));
            uint generation=(uint)Property(victim,"LifeGeneration");Component wave=Effect("Wave",p,4,2.2f,.55f,0,2);Call(wave,"Exclude",new Dictionary<Component,uint>{{victim,generation}});
            victim.gameObject.SetActive(false);victim.gameObject.SetActive(true);Set(victim,"_currentHealth",100f);Physics2D.SyncTransforms();yield return new WaitForSeconds(.2f);Assert.AreEqual(96f,Property(victim,"CurrentHealth"));
        }

        /// <summary>第三拳余波排除本次直接受击者；短刀残影固定在原位置产生第二段伤害。</summary>
        [UnityTest] public IEnumerator PistonThirdAttackAndEcho_ProduceDistinctSecondaryHits()
        {
            Component weapon=Equip("11_piston_gauntlet");Set(weapon,"_currentCooldown",99f);yield return null;
            object aim=_player.GetComponent(T("AimController"));Call(aim,"SetMode",Enum.Parse(T("AimController+AimMode"),"Manual"));Call(aim,"SetManualDirection",Vector2.right);
            var near=Enemy(weapon.transform.position+Vector3.right*.7f);var far=Enemy(weapon.transform.position+Vector3.right*2f);Physics2D.SyncTransforms();
            for(int i=0;i<3;i++){Call(weapon,"Attack");yield return new WaitForSeconds(.65f);Assert.AreEqual(i<2?100f:96f,Property(far,"CurrentHealth"));}
            Assert.AreEqual(76f,Property(near,"CurrentHealth"));Call(_loadout,"RemoveRoundWeapon",weapon);near.gameObject.SetActive(false);far.gameObject.SetActive(false);
            weapon=Equip("19_echo_dagger");Set(weapon,"_currentCooldown",99f);yield return null;
            var target=Enemy(weapon.transform.position+Vector3.right*.65f);Physics2D.SyncTransforms();Call(weapon,"Attack");
            yield return new WaitForSeconds(.25f);Assert.AreEqual(95f,Property(target,"CurrentHealth"));
            _player.transform.position+=Vector3.up*2;yield return new WaitForSeconds(.45f);Assert.AreEqual(92f,Property(target,"CurrentHealth"));
        }

        /// <summary>炮台从正式道具出手，独立工程伤害和冷却不受普通战斗增益影响。</summary>
        [UnityTest] public IEnumerator SentryDamageAndCooling_IgnoreOrdinaryCombatModifiers()
        {
            object manager=_player.GetComponent(T("AbilityManager"));Call(manager,"GrantOrUpgrade",Item("copper_sentry"));yield return null;yield return null;
            Array modifiers=Array.CreateInstance(T("PlayerStatModifier"),5);string[] names={"Engineering","DamagePercent","CritChance","AttackSpeed","Range"};
            for(int i=0;i<names.Length;i++)modifiers.SetValue(Activator.CreateInstance(T("PlayerStatModifier"),Enum.Parse(T("PlayerStatType"),names[i]),Enum.Parse(T("PlayerStatModifierMode"),"Flat"),i==0?20f:500f),i);
            Call(_player,"SetModifiers","expansion.test",modifiers);
            Component tower=(Component)UnityEngine.Object.FindObjectOfType(T("EngineeringStructure"));var victim=Enemy(tower.transform.position+Vector3.right);Physics2D.SyncTransforms();
            Set(tower,"_remaining",.02f);Set(tower,"_nextSearch",0f);yield return new WaitForSeconds(.22f);Assert.AreEqual(86f,Property(victim,"CurrentHealth"));
            Assert.AreEqual(1f,Field(tower,"_interval"));float remaining=(float)Property(tower,"RemainingCooldown");
            Call(manager,"GrantOrUpgrade",Item("cooling_fan"));yield return null;
            Assert.That((float)Property(tower,"RemainingCooldown"),Is.EqualTo(remaining/1.15f-Time.deltaTime).Within(.04));
            object loadout=_player.GetComponent(T("EngineeringLoadout"));object entry=((IList)Field(loadout,"_entries"))[0];
            Call(loadout,"SetCount",entry,2);yield return null;Assert.AreEqual(2,UnityEngine.Object.FindObjectsOfType(T("EngineeringStructure")).Length);
            Call(loadout,"SetCount",entry,1);yield return null;Assert.IsTrue(tower.gameObject.activeSelf);Assert.AreEqual(1,UnityEngine.Object.FindObjectsOfType(T("EngineeringStructure")).Length);
            Call(loadout,"Remove",entry);yield return null;Assert.AreEqual(0,UnityEngine.Object.FindObjectsOfType(T("EngineeringStructure")).Length);
        }

        /// <summary>当前开放的扩展武器在空场待机，有目标后实际造成伤害；鞭子暂时退出可获取池。</summary>
        [UnityTest] public IEnumerator AllTenWeapons_RangeGateAttackAndCleanUp()
        {
            foreach(string id in new[]{"11_piston_gauntlet","12_coil_railgun","13_arc_welder","14_clockwork_minecase","15_homing_scythe","16_frost_chime","18_split_seedpod","19_echo_dagger","20_ink_tide_brush"})
            {
                Component weapon=Equip(id);Set(weapon,"_currentCooldown",0f);
                yield return new WaitForSeconds(.12f);
                Assert.AreEqual(0,UnityEngine.Object.FindObjectsOfType(T("ExpansionEffect")).Length,id+" 空场必须待机");
                Assert.AreEqual(0,UnityEngine.Object.FindObjectsOfType(T("MeleeSwingHitbox")).Length,id+" 空场近战必须待机");
                object aim=_player.GetComponent(T("AimController"));Call(aim,"SetMode",Enum.Parse(T("AimController+AimMode"),"NearestEnemy"));Call(aim,"SetManualDirection",Vector2.right);
                Component enemy=Enemy(weapon.transform.position+Vector3.right*.65f);Physics2D.SyncTransforms();
                yield return new WaitForSeconds(.09f);Set(weapon,"_currentCooldown",99f);
                yield return Capture(id);
                yield return new WaitForSeconds(1.65f);
                Assert.Less((float)Property(enemy,"CurrentHealth"),100f,id+" 应命中近处目标");
                Call(_loadout,"RemoveRoundWeapon",weapon);yield return null;
                Assert.AreEqual(0,UnityEngine.Object.FindObjectsOfType(T("ExpansionEffect")).Length,id+" 移除应回收全部派生效果");
                enemy.gameObject.SetActive(false);
            }
        }

        /// <summary>射束等待蓄能并按沿线顺序最多命中三名；喷射固定结算三次。</summary>
        [UnityTest] public IEnumerator RailgunAndWelder_DelaysLimitsAndThreeTicks()
        {
            Vector3 p=new Vector3(4,0);var enemies=new List<Component>();for(int i=1;i<=4;i++)enemies.Add(Enemy(p+Vector3.right*i));Physics2D.SyncTransforms();
            Component effect=Effect("Beam",p,10,7,.16f,.3f,3);
            yield return new WaitForSeconds(.2f);Assert.AreEqual(100f,Property(enemies[0],"CurrentHealth"));
            yield return new WaitForSeconds(.2f);
            for(int i=0;i<4;i++)Assert.AreEqual(i<3?90f:100f,Property(enemies[i],"CurrentHealth"));
            foreach(var e in enemies)e.gameObject.SetActive(false);
            var victim=Enemy(p+Vector3.right);Physics2D.SyncTransforms();Effect("Cone",p,3,2.2f,60);
            yield return new WaitForSeconds(.7f);Assert.AreEqual(91f,Property(victim,"CurrentHealth"));
        }

        /// <summary>地雷落地后仍需布防；迫击炮锁定落点并等待固定飞行时间。</summary>
        [UnityTest] public IEnumerator MineAndMortar_ArmAndLockGroundPoint()
        {
            Vector3 p=new Vector3(4,0);var victim=Enemy(p+Vector3.right);Physics2D.SyncTransforms();
            Effect("Mine",p,14,4.5f,1,0,0,(Vector2)(p+Vector3.right));
            yield return new WaitForSeconds(.55f);Assert.AreEqual(100f,Property(victim,"CurrentHealth"));
            yield return new WaitForSeconds(.3f);Assert.AreEqual(86f,Property(victim,"CurrentHealth"));
            Effect("Mortar",p,18,7,1.1f,0,0,(Vector2)(p+Vector3.right));
            yield return new WaitForSeconds(.5f);Assert.AreEqual(86f,Property(victim,"CurrentHealth"));
            victim.transform.position=p+Vector3.up*3;yield return new WaitForSeconds(.3f);Assert.AreEqual(86f,Property(victim,"CurrentHealth"));
        }

        /// <summary>回旋去回各命中一次；母种分裂三颗，不递归、不重击母种受害者。</summary>
        [UnityTest] public IEnumerator ReturningAndSeed_IndependentLegsAndBoundedChildren()
        {
            Vector3 p=_player.transform.position;var victim=Enemy(p+Vector3.right);Physics2D.SyncTransforms();
            Effect("Returning",p,6,2,.2f);yield return new WaitForSeconds(.6f);Assert.AreEqual(88f,Property(victim,"CurrentHealth"));
            Set(victim,"_currentHealth",100f);Effect("Seed",p,8,5.5f,.14f);
            yield return new WaitForSeconds(.12f);Assert.AreEqual(92f,Property(victim,"CurrentHealth"));
            int children=0;foreach(Component e in UnityEngine.Object.FindObjectsOfType(T("ExpansionEffect")))if(Property(e,"Kind").ToString()=="SeedChild")children++;
            Assert.AreEqual(3,children);yield return new WaitForSeconds(.3f);Assert.AreEqual(92f,Property(victim,"CurrentHealth"));
        }

        /// <summary>同位墨带只结算较强三跳；减速分别到期，灼烧不丢自然到期末跳。</summary>
        [UnityTest] public IEnumerator StatusAndInk_StrongestOnlyAndCompleteTicks()
        {
            Vector3 p=new Vector3(4,0);var victim=Enemy(p+Vector3.right);Physics2D.SyncTransforms();
            Effect("Ink",p,2,3.5f,.65f);Effect("Ink",p,5,3.5f,.65f);
            yield return new WaitForSeconds(1.6f);Assert.AreEqual(85f,Property(victim,"CurrentHealth"));
            object status=Property(victim,"CombatStatus");
            Call(status,"ApplyBurn",_player.transform,4f,null);Call(status,"ApplyBurn",((Component)_round).transform,2f,null);
            yield return new WaitForSeconds(1.65f);Assert.AreEqual(73f,Property(victim,"CurrentHealth"));
            Call(status,"ApplySlow",_player.transform,.3f,.2f);Call(status,"ApplySlow",((Component)_round).transform,.1f,.5f);
            yield return null;Assert.That((float)Property(status,"SpeedFactor"),Is.EqualTo(.7f).Within(.001));
            yield return new WaitForSeconds(.25f);Assert.That((float)Property(status,"SpeedFactor"),Is.EqualTo(.9f).Within(.001));
            victim.gameObject.SetActive(false);victim.gameObject.SetActive(true);Assert.AreEqual(1f,Property(status,"SpeedFactor"));
        }

        /// <summary>链击选择四名不同敌人，逐段衰减且不重复回跳。</summary>
        [UnityTest] public IEnumerator NexusChain_HitsFourUniqueTargetsWithFalloff()
        {
            Vector3 p=new Vector3(4,0);var enemies=new List<Component>();for(int i=1;i<=5;i++)enemies.Add(Enemy(p+Vector3.right*i));Physics2D.SyncTransforms();
            Effect("Chain",p,20,4.8f,.1f);yield return null;
            float[] hp={80,84,88,90,100};for(int i=0;i<5;i++)Assert.AreEqual(hp[i],Property(enemies[i],"CurrentHealth"));
        }

        /// <summary>四档建筑按正式道具副本部署八座，不占武器槽；暂停、攻速更新和波次清理稳定。</summary>
        [UnityTest] public IEnumerator Structures_EightIndependentDeploymentsAndPauseCleanup()
        {
            object manager=_player.GetComponent(T("AbilityManager"));
            foreach(string id in new[]{"copper_sentry","frost_sprayer","shell_mortar","arc_nexus"})
            {object item=Item(id);for(int i=0;i<(int)Field(item,"maxCopies");i++)Assert.NotNull(Call(manager,"GrantOrUpgrade",item));Assert.IsNull(Call(manager,"GrantOrUpgrade",item));}
            yield return null;yield return null;
            var structures=UnityEngine.Object.FindObjectsOfType(T("EngineeringStructure"));Assert.AreEqual(8,structures.Length);Assert.AreEqual(0,((IList)Property(_loadout,"OwnedWeapons")).Count);
            for(int i=0;i<8;i++)for(int j=i+1;j<8;j++)Assert.Greater(Vector2.Distance(((Component)structures[i]).transform.position,((Component)structures[j]).transform.position),.9f);
            Time.timeScale=0;float cooldown=(float)Property(structures[0],"RemainingCooldown");yield return new WaitForSecondsRealtime(.12f);Assert.AreEqual(cooldown,Property(structures[0],"RemainingCooldown"));Assert.AreEqual(8,UnityEngine.Object.FindObjectsOfType(T("EngineeringStructure")).Length);Time.timeScale=1;
            object fan=Item("cooling_fan");Assert.NotNull(Call(manager,"GrantOrUpgrade",fan));yield return null;
            Assert.AreEqual(15f,Property(_player.GetComponent(T("EngineeringLoadout")),"AttackSpeedBonus"));
            yield return Capture("eight-structures");
            _round.GetType().GetProperty("Phase").SetValue(_round,Enum.Parse(T("RoundPhase"),"Shop"));
            yield return null;yield return null;Assert.AreEqual(0,UnityEngine.Object.FindObjectsOfType(T("EngineeringStructure")).Length);Assert.AreEqual(0,UnityEngine.Object.FindObjectsOfType(T("ExpansionEffect")).Length);
        }

        /// <summary>跨来源复用同一个效果对象，不能误阻塞原武器。</summary>
        [UnityTest] public IEnumerator PoolReuse_DoesNotBlockForeignWeapon()
        {
            Component weapon=Equip("12_coil_railgun");Set(weapon,"_currentCooldown",99f);
            Component effect=Effect("Beam",new Vector3(4,0),10,7,.1f,5);Set(weapon,"_blocking",effect);
            var property=weapon.GetType().GetProperty("CanStartAttack",Flags);Assert.IsTrue((bool)property.GetValue(weapon));
            yield return null;Call(_loadout,"RemoveRoundWeapon",weapon);Assert.IsTrue(effect.gameObject.activeSelf);
        }

        /// <summary>创建正式池化效果并填写本次发射快照，避免另写测试攻击算法。</summary>
        private Component Effect(string kind,Vector3 position,float damage,float range,float width,float delay=0,int maxHits=0,Vector2? target=null)
        {
            object data=_data["12_coil_railgun"];var go=(GameObject)Call(_pool,"Spawn",Field(data,"expansionEffectPrefab"),position,Quaternion.identity);Component effect=go.GetComponent(T("ExpansionEffect"));
            object spec=Activator.CreateInstance(T("EffectSpec"));Set(spec,"Kind",Enum.Parse(T("ExpansionEffectKind"),kind));var source=new GameObject("EffectSource");source.transform.position=position;_fixtures.Add(source);Set(spec,"Source",source.transform);Set(spec,"Owner",_player.transform);Set(spec,"Damage",damage);Set(spec,"Range",range);Set(spec,"Width",width);Set(spec,"Delay",delay);Set(spec,"MaxHits",maxHits);Set(spec,"Speed",10f);Set(spec,"Tier",1);Set(spec,"Duration",kind=="Mine"?6f:1.5f);Set(spec,"Direction",Vector2.right);Set(spec,"Target",target??(Vector2)(position+Vector3.right));
            Call(effect,"Configure",spec);return effect;
        }
        /// <summary>从正式商店配置解析道具，无测试专属替代品。</summary>
        private object Item(string id)
        {
            foreach(object product in (IEnumerable)Field(Field(Field(_round,"config"),"shopCatalog"),"products"))
            {object data=Field(Field(product,"content"),"abilityToGrant");if(data!=null&&(string)Field(data,"abilityID")==id)return data;}
            throw new Exception("Missing item "+id);
        }

        /// <summary>创建静止的真实受击目标，位于测试专用区域。</summary>
        private Component Enemy(Vector3 position)
        {
            var go=new GameObject("Session26Target"); _fixtures.Add(go); go.SetActive(false); go.layer=LayerMask.NameToLayer("Enemy"); go.transform.position=position;
            var body=go.AddComponent<Rigidbody2D>(); body.gravityScale=0; body.constraints=RigidbodyConstraints2D.FreezeAll;
            go.AddComponent<CircleCollider2D>().radius=.25f;
            var enemy=(Behaviour)go.AddComponent(T("EnemyBase")); go.SetActive(true);
            Set(enemy,"_currentHealth",100f); return enemy;
        }
        /// <summary>通过正式授予入口创建独立装备。</summary>
        private Component Equip(string id) { return (Component)Call(_loadout,"BuyRoundWeapon",_data[id],1); }
        /// <summary>通过正式对象池取出实际弹体。</summary>
        private GameObject Spawn(string id,Vector3 position) { return (GameObject)Call(_pool,"Spawn",Field(_data[id],"projectilePrefab"),position,Quaternion.identity); }
        /// <summary>依据武器来源找到本次实际生成的攻击。</summary>
        private static Component FindAttack(string type,object data)
        { foreach(Component attack in UnityEngine.Object.FindObjectsOfType(T(type))) if(ReferenceEquals(Field(attack,type=="AuraDamageZone"||type=="ProjectileBase"?"weaponData":"_weaponData"),data)) return attack; return null; }
        /// <summary>图形环境下记录相机画面，无图形完整门禁保留行为断言。</summary>
        private static IEnumerator Capture(string name)
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            yield return null;
            string directory=Path.GetFullPath("Logs/Session26/EngineeringExpansion/Graphics"); Directory.CreateDirectory(directory);
            Camera camera=Camera.main; var target=new RenderTexture(1280,720,24); var old=camera.targetTexture; var active=RenderTexture.active;
            var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
            try {camera.targetTexture=target;camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(directory,name+".png"),pixels.EncodeToPNG());}
            finally {camera.targetTexture=old;RenderTexture.active=active;UnityEngine.Object.Destroy(pixels);target.Release();UnityEngine.Object.Destroy(target);}
        }
        /// <summary>解析生产程序集类型。</summary>
        private static Type T(string name) { return Type.GetType(name+", Assembly-CSharp",true); }
        /// <summary>读取运行时字段，包括继承层私有字段。</summary>
        private static object Field(object obj,string name)
        { for(Type t=obj.GetType();t!=null;t=t.BaseType) {var f=t.GetField(name,Flags);if(f!=null)return f.GetValue(obj);} throw new MissingFieldException(name); }
        /// <summary>设置测试快照字段。</summary>
        private static void Set(object obj,string name,object value) { for(Type t=obj.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,Flags);if(f!=null){f.SetValue(obj,value);return;}} throw new MissingFieldException(name); }
        /// <summary>读取公开状态。</summary>
        private static object Property(object obj,string name) { return obj.GetType().GetProperty(name).GetValue(obj); }
        /// <summary>按完整参数数量选择生产入口，避免调用歧义。</summary>
        private static object Call(object obj,string name,params object[] args)
        {foreach(var method in obj.GetType().GetMethods(Flags)) if(method.Name==name&&method.GetParameters().Length==args.Length)return method.Invoke(obj,args);throw new MissingMethodException(name);}
    }
}
