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
    public sealed class WeaponCombatPlayModeTests
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
            Scene empty=SceneManager.CreateScene("WeaponCombatEmpty"); SceneManager.SetActiveScene(empty);
            Scene main=SceneManager.GetSceneByName("MainLevel"); if(main.isLoaded) yield return SceneManager.UnloadSceneAsync(main);
        }

        /// <summary>真实碰撞后飞梭依次改变方向并命中两个后续目标；剩余次数为零时也不重复伤害已命中实体。</summary>
        [UnityTest] public IEnumerator TrackingBounce_TurnsAndHitsTwoFurtherEnemies()
        {
            Vector3 origin=new Vector3(4,0);
            Component first=Enemy(origin+Vector3.right), second=Enemy(origin+new Vector3(1,2)), third=Enemy(origin+new Vector3(4,2));
            GameObject shot=Spawn("06_prism_shard",origin);
            object projectile=shot.GetComponent(T("ProjectileBase"));
            Call(projectile,"Initialize",_data["06_prism_shard"],Vector3.right,10f,5f,0,.3f,2,
                Enum.Parse(T("BounceMode"),"Tracking"),1f,Activator.CreateInstance(T("WeaponHitSnapshot")));
            for(int i=0;i<120 && (float)Property(first,"CurrentHealth")>=100;i++) yield return new WaitForFixedUpdate();
            Assert.AreEqual(90f,Property(first,"CurrentHealth"));
            Vector3 direction=(Vector3)Property(projectile,"FlightDirection"); Assert.Greater(direction.y,.9f,"首次命中后必须向上转向");
            second.transform.position+=Vector3.right*.2f;
            for(int i=0;i<160 && (float)Property(third,"CurrentHealth")>=100;i++) yield return new WaitForFixedUpdate();
            Assert.AreEqual(90f,Property(second,"CurrentHealth")); Assert.AreEqual(90f,Property(third,"CurrentHealth"));
            Assert.AreEqual(90f,Property(first,"CurrentHealth")); Assert.IsFalse(shot.activeSelf);
            // 同一池重新取出时应重置目标、方向和命中名单。
            GameObject reused=Spawn("06_prism_shard",origin);
            Call(reused.GetComponent(T("ProjectileBase")),"Initialize",_data["06_prism_shard"],Vector3.left);
            Assert.AreEqual(Vector3.left,Property(reused.GetComponent(T("ProjectileBase")),"FlightDirection"));
        }

        /// <summary>弹射目标在飞行中销毁后必须重新选敌，不能沿失效引用方向继续飞走。</summary>
        [UnityTest] public IEnumerator TrackingBounce_DestroyedTarget_ReacquiresAndHits()
        {
            Vector3 origin=new Vector3(4,0);
            Component first=Enemy(origin+Vector3.right), second=Enemy(origin+new Vector3(1,2)), replacement=Enemy(origin+new Vector3(4,-1));
            GameObject shot=Spawn("06_prism_shard",origin); object projectile=shot.GetComponent(T("ProjectileBase"));
            Call(projectile,"Initialize",_data["06_prism_shard"],Vector3.right,10f,5f,0,.3f,2,
                Enum.Parse(T("BounceMode"),"Tracking"),1f,Activator.CreateInstance(T("WeaponHitSnapshot")));
            for(int i=0;i<120 && (float)Property(first,"CurrentHealth")>=100;i++) yield return new WaitForFixedUpdate();
            Assert.AreEqual(90f,Property(first,"CurrentHealth"));
            UnityEngine.Object.Destroy(second.gameObject); yield return null; yield return null;
            Assert.Less(((Vector3)Property(projectile,"FlightDirection")).y,0);
            for(int i=0;i<160 && (float)Property(replacement,"CurrentHealth")>=100;i++) yield return new WaitForFixedUpdate();
            Assert.AreEqual(90f,Property(replacement,"CurrentHealth"));
        }

        /// <summary>两件斜向素材在八个发射方向均保持尖端朝前，使用实际 Prefab 的可见子渲染器。</summary>
        [UnityTest] public IEnumerator NeedleAndPrism_VisualTipMatchesEightFlightDirections()
        {
            foreach(string id in new[]{"07_medical_lancet","06_prism_shard"})
            for(int i=0;i<8;i++)
            {
                Vector3 direction=Quaternion.Euler(0,0,i*45f)*Vector3.right;
                GameObject shot=Spawn(id,new Vector3(60,60));
                Call(shot.GetComponent(T("ProjectileBase")),"Initialize",_data[id],direction);
                SpriteRenderer visual=null; foreach(var sr in shot.GetComponentsInChildren<SpriteRenderer>()) if(sr.enabled) visual=sr;
                Assert.NotNull(visual);
                Vector3 tip=visual.transform.TransformDirection(new Vector3(1,1).normalized);
                Assert.That(Vector3.Dot(tip,direction),Is.GreaterThan(.999f),id+" direction "+i);
                Call(_pool,"Release",Field(_data[id],"projectilePrefab"),shot);
            }
            yield return null;
        }

        /// <summary>从正式武器发射的陨铁锤超过旧的一秒寿命仍存活，完整离屏后才回收。</summary>
        [UnityTest] public IEnumerator Hammer_SurvivesOldTimeout_AndRecyclesOutsideView()
        {
            Component weapon=Equip("10_meteor_hammer"); ((Behaviour)weapon).enabled=false;
            object aim=_player.GetComponent(T("AimController")); Call(aim,"SetMode",Enum.Parse(T("AimController+AimMode"),"Manual")); Call(aim,"SetManualDirection",Vector2.up);
            Call(weapon,"Attack"); Component shot=FindAttack("LobbedProjectile",_data["10_meteor_hammer"]); Assert.NotNull(shot);
            yield return new WaitForSeconds(1.2f); Assert.IsTrue(shot.gameObject.activeSelf,"仍在屏内不能按射程/速度回收");
            Assert.Greater(Camera.main.WorldToViewportPoint(shot.transform.position).y,0f);
            yield return Capture("hammer-after-1.2s");
            shot.transform.position=new Vector3(Camera.main.transform.position.x,Camera.main.transform.position.y+100,0);
            Assert.IsFalse((bool)Call(shot,"IsCompletelyOutsideGameView"),"顶部离屏允许回落");
            Set(shot,"_startPosition",new Vector3(Camera.main.transform.position.x+100,Camera.main.transform.position.y,0));
            yield return null; Assert.IsFalse(shot.gameObject.activeSelf);
            // 正下方输入也必须实际朝下发射，而非回退水平。
            Call(aim,"SetManualDirection",Vector2.down); Call(weapon,"Attack");
            Component downward=FindAttack("LobbedProjectile",_data["10_meteor_hammer"]);
            Assert.Less(((Vector3)Field(downward,"_initialVelocity")).y,0f);
        }

        /// <summary>长枪每把独立交替，从挂点连续出手；手动瞄准以玩家为原点，回合重置从突刺开始。</summary>
        [UnityTest] public IEnumerator Spear_ThrustSweepThrust_FromOwnMount()
        {
            Component spear=Equip("03_tide_spear"); ((Behaviour)spear).enabled=false;
            Component second=Equip("03_tide_spear"); ((Behaviour)second).enabled=false;
            object aim=_player.GetComponent(T("AimController")); Call(aim,"SetMode",Enum.Parse(T("AimController+AimMode"),"Manual")); Call(aim,"SetManualDirection",Vector2.up);
            yield return new WaitForSeconds(.4f);
            // 横挥在中段伸出再转刃，旧的贴脸位置不属于新刀身路径；使用两种动作都会经过的刀身中心。
            float sharedReach=.75f*(float)Property(spear,"CurrentVisualRange")+.05f+.5f*(float)Property(spear,"CurrentVisualLength");
            Component victim=Enemy(spear.transform.position+Vector3.up*sharedReach);Physics2D.SyncTransforms();
            for(int i=0;i<3;i++)
            {
                Set(victim,"_currentHealth",100f);
                Assert.AreEqual(i%2==0,Property(spear,"NextAttackIsThrust"));
                Call(spear,"Attack"); Component hit=FindAttack("MeleeSwingHitbox",_data["03_tide_spear"]);
                Assert.NotNull(hit); Assert.Less(Vector3.Distance(spear.transform.position,hit.transform.position),.001f);
                Assert.AreEqual(i%2==0,Field(hit,"_thrust"));
                if(i%2==0) Assert.That(Vector3.Dot(hit.transform.right,Vector3.up),Is.GreaterThan(.99));
                yield return new WaitForSeconds(.045f);
                yield return Capture(i%2==0?"spear-thrust-"+i:"spear-sweep");
                yield return new WaitForSeconds((float)Field(hit,"_duration"));
                float damage=(float)Call(spear,"GetCurrentDamage");
                Assert.That((float)Property(victim,"CurrentHealth"),Is.EqualTo(100f-damage).Within(.001),"动作 "+i+" 应准确命中一次");
            }
            Assert.IsTrue((bool)Property(second,"NextAttackIsThrust"));
            Call(spear,"ResetRoundCooldown"); Assert.IsTrue((bool)Property(spear,"NextAttackIsThrust"));
        }

        /// <summary>正式六槽展示、移除后重排，环绕和光环仍绑定玩家中心而非偏移挂点。</summary>
        [UnityTest] public IEnumerator SixWeapons_MountsAndPersistentEffectsKeepCorrectCenters()
        {
            foreach(string id in new[]{"01_copper_rapier","03_tide_spear","05_rivet_spike","07_medical_lancet","08_ember_censer","09_moon_disc"}) Equip(id);
            Component censer=null;
            foreach(Component weapon in (IList)Property(_loadout,"OwnedWeapons"))
                if(ReferenceEquals(Field(weapon,"weaponData"),_data["08_ember_censer"])) censer=weapon;
            Call(censer,"Attack");
            Component firstAura=FindAttack("AuraDamageZone",_data["08_ember_censer"]);
            Assert.That(Vector3.Distance(firstAura.transform.position,_player.transform.position),Is.LessThan(.001),"光环生成当帧就必须位于玩家中心");
            yield return null;
            var weapons=(IList)Property(_loadout,"OwnedWeapons"); Assert.AreEqual(6,weapons.Count);
            int mountIndex=0;
            foreach(Component weapon in weapons)
            {
                bool mounted=(bool)Property(weapon,"UsesHeldMount");
                Assert.That(weapon.transform.localPosition.magnitude,Is.EqualTo(mounted?.72f:0f).Within(.001));
                if(mounted)
                {
                    float angle=Mathf.Repeat(Mathf.Atan2(weapon.transform.localPosition.y,weapon.transform.localPosition.x)*Mathf.Rad2Deg,360f);
                    Assert.That(angle,Is.EqualTo(90f*mountIndex++).Within(.001));
                }
                else Assert.IsNull(weapon.transform.Find("HeldVisual"));
            }
            Assert.AreEqual(4,mountIndex);
            Component aura=FindAttack("AuraDamageZone",_data["08_ember_censer"]);
            Component orbiter=FindAttack("OrbitingProjectile",_data["09_moon_disc"]);
            Assert.AreSame(_player.transform,Field(aura,"followTarget")); Assert.AreSame(_player.transform,Field(orbiter,"_owner"));
            yield return Capture("six-weapons");
            Call(_loadout,"RemoveRoundWeapon",weapons[0]); yield return null;
            Assert.AreEqual(5,weapons.Count);
            Assert.That(Vector3.Angle(((Component)weapons[0]).transform.localPosition,((Component)weapons[1]).transform.localPosition),Is.EqualTo(120).Within(.01));
            yield return Capture("five-weapons");
            while(weapons.Count>2) Call(_loadout,"RemoveRoundWeapon",weapons[0]);
            foreach(Component weapon in weapons) { Assert.AreEqual(Vector3.zero,weapon.transform.localPosition); Assert.IsNull(weapon.transform.Find("HeldVisual")); }
            Component added=Equip("07_medical_lancet"); Assert.That(added.transform.localPosition.x,Is.EqualTo(.72f).Within(.001));
        }

        /// <summary>通过 F9 实际修改射程，验证持武、突刺、横扫、回收及飞行素材使用同一尺寸。</summary>
        [UnityTest] public IEnumerator F9_RangeChanges_KeepMeleeSizeAndExtendTravel()
        {
            Component spear=Equip("03_tide_spear");
            // 保持持武启用，但禁用自动冷却推进；手动调用正式攻击入口。
            Set(spear,"_currentCooldown",100f);
            var view=(Component)spear.GetComponent(T("WeaponHeldView"));
            var held=(SpriteRenderer)Property(view,"Renderer");
            yield return new WaitForSeconds(.3f);
            float original=held.transform.lossyScale.x;
            Component panel=(Component)UnityEngine.Object.FindObjectOfType(T("PlayerAttributeDebugPanel"));
            if(panel==null) {var go=new GameObject("F9Fixture");_fixtures.Add(go);panel=go.AddComponent(T("PlayerAttributeDebugPanel"));}
            object range=Enum.Parse(T("PlayerStatType"),"Range"), flat=Enum.Parse(T("PlayerStatModifierMode"),"Flat");
            Time.timeScale=0f;
            Assert.IsTrue((bool)Call(panel,"DebugSetModifier",range,flat,200f));
            Assert.AreEqual(200f,Call(_player.GetComponent(T("PlayerStats")),"GetFinalStat",range));
            Time.timeScale=1f;
            yield return new WaitForSeconds(.4f);
            float enlarged=held.transform.lossyScale.x;
            Assert.That(enlarged,Is.EqualTo(original).Within(.001),"范围只改变旅程，不能拉长刀身");
            yield return Capture("revision-spear-held-range");
            for(int i=0;i<2;i++)
            {
                if(i==1)
                {
                    float beforeLength=(float)Property(spear,"CurrentVisualLength");
                    Call(panel,"DebugSetModifier",range,flat,400f);
                    enlarged *= (float)Property(spear,"CurrentVisualLength") / beforeLength;
                }
                Call(spear,"Attack"); Component hit=FindAttack("MeleeSwingHitbox",_data["03_tide_spear"]);
                var visual=(SpriteRenderer)Property(hit,"VisualRenderer");
                Assert.AreSame(held.sprite,visual.sprite);
                Assert.That(visual.transform.lossyScale.x,Is.EqualTo(held.transform.lossyScale.x).Within(.001),"发起动作不能跳变尺寸");
                Assert.That(Vector3.Distance(visual.transform.position,held.transform.position),Is.LessThan(.001));
                yield return new WaitForSeconds(.1f);
                Assert.That(visual.transform.lossyScale.x,Is.EqualTo(enlarged).Within(.002));
                yield return Capture(i==0?"revision-spear-thrust-range":"revision-spear-sweep-range");
                yield return new WaitForSeconds((float)Field(hit,"_duration") + .05f);
                Assert.IsTrue(held.enabled);
                Assert.That(held.transform.lossyScale.x,Is.EqualTo(enlarged).Within(.002),"回到持武不能跳变尺寸");
            }
            Call(_loadout,"RemoveRoundWeapon",spear);
            foreach(string id in new[]{"07_medical_lancet","06_prism_shard","10_meteor_hammer"})
            {
                Component weapon=Equip(id);Set(weapon,"_currentCooldown",100f);
                yield return new WaitForSeconds(.3f);
                var weaponHeld=(SpriteRenderer)Property(weapon.GetComponent(T("WeaponHeldView")),"Renderer");
                if(id=="07_medical_lancet") Call(panel,"DebugSetModifier",range,flat,600f);
                Call(weapon,"Attack");
                string type=id=="10_meteor_hammer"?"LobbedProjectile":"ProjectileBase";
                Component shot=FindAttack(type,_data[id]); Assert.NotNull(shot);
                SpriteRenderer shotVisual=null; foreach(var sr in shot.GetComponentsInChildren<SpriteRenderer>()) if(sr.enabled)shotVisual=sr;
                Assert.AreSame(weaponHeld.sprite,shotVisual.sprite);
                Assert.That(shotVisual.transform.lossyScale.x,Is.EqualTo(weaponHeld.transform.lossyScale.x).Within(.002),id);
                Call(_pool,"Release",Field(_data[id],"projectilePrefab"),shot.gameObject);
                Call(_loadout,"RemoveRoundWeapon",weapon);
            }
            Assert.IsTrue((bool)Call(panel,"DebugClearModifiers"));
            Set(panel,"_visible",true);
            if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null && !Application.isBatchMode)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/Session26/Feedback2Graphics/revision-f9.png"));
                yield return null;yield return null;
            }
            Set(panel,"_visible",false);
        }

        /// <summary>八件主动武器逐一验证空场待机、范围外等待、进入后出手、离开及死亡后停火。</summary>
        [UnityTest] public IEnumerator ActiveWeapons_OnlyFireWhenLivingEnemyEntersActualRange()
        {
            foreach(string id in new[]{"01_copper_rapier","02_saw_cleaver","03_tide_spear","04_briar_bolt","05_rivet_spike","06_prism_shard","07_medical_lancet","10_meteor_hammer"})
            {
                Component weapon=Equip(id);
                string attackType=id=="10_meteor_hammer"?"LobbedProjectile":id.StartsWith("01")||id.StartsWith("02")||id.StartsWith("03")?"MeleeSwingHitbox":"ProjectileBase";
                yield return new WaitForSeconds(.15f);
                Assert.IsNull(FindAttack(attackType,_data[id]),id+" 空场不应攻击");
                Assert.AreEqual(0f,Field(weapon,"_currentCooldown"),"空场保留已就绪冷却");
                float range=(float)Property(weapon,"CurrentAttackRange");
                Vector3 origin=(Vector2)Property(weapon,"AttackOrigin");
                Component target=Enemy(origin+Vector3.right*(range+.6f));
                Physics2D.SyncTransforms(); yield return new WaitForSeconds(.12f);
                Assert.IsNull(FindAttack(attackType,_data[id]),id+" 范围外不应攻击");
                // 接近但不与玩家接触，通过实际 Update 而不是直接调用 Attack。
                target.transform.position=origin+Vector3.right*Mathf.Max(.5f,range-.15f);
                Physics2D.SyncTransforms(); yield return new WaitForSeconds(.12f);
                Component shot=FindAttack(attackType,_data[id]); Assert.NotNull(shot,id+" 进入范围应出手");
                Assert.Greater((float)Field(weapon,"_currentCooldown"),0f);
                // 铆钉枪一轮有三发，清理整轮已发弹体，再验证有没有新一轮攻击。
                while((shot=FindAttack(attackType,_data[id]))!=null)
                    Call(_pool,"Release",Field(_data[id],"projectilePrefab"),shot.gameObject);
                target.transform.position=origin+Vector3.right*(range+1f);
                Physics2D.SyncTransforms(); Set(weapon,"_currentCooldown",0f);
                yield return new WaitForSeconds(.12f);
                Assert.IsNull(FindAttack(attackType,_data[id]),id+" 离开范围后不应续发");
                target.transform.position=weapon.transform.position+Vector3.right*.8f;
                Set(target,"_currentHealth",0f); Physics2D.SyncTransforms();
                yield return new WaitForSeconds(.12f);
                Assert.IsNull(FindAttack(attackType,_data[id]),id+" 已死但尚未回池的敌人不触发攻击");
                target.gameObject.SetActive(false);
                Call(_loadout,"RemoveRoundWeapon",weapon);
            }
            // 常驻例外在无敌人场景仍保持自己的伤害实体。
            Equip("08_ember_censer");Equip("09_moon_disc");yield return new WaitForSeconds(.12f);
            Assert.NotNull(FindAttack("AuraDamageZone",_data["08_ember_censer"]));
            Assert.NotNull(FindAttack("OrbitingProjectile",_data["09_moon_disc"]));
        }

        /// <summary>左右完整主动段采用镜像平移和 324 度局部旋转，不能退回固定圆心模型。</summary>
        [UnityTest] public IEnumerator MeleeSweeps_FullTrajectoriesMirrorAroundMount()
        {
            object aim=_player.GetComponent(T("AimController"));Call(aim,"SetMode",Enum.Parse(T("AimController+AimMode"),"Manual"));
            Vector3[] right=new Vector3[41];
            foreach(int side in new[]{1,-1})
            {
                Call(aim,"SetManualDirection",Vector2.right*side);
                Component weapon=Equip("02_saw_cleaver");Set(weapon,"_currentCooldown",99f);
                yield return new WaitForSeconds(.3f);
                Call(weapon,"Attack");Component hit=FindAttack("MeleeSwingHitbox",_data["02_saw_cleaver"]);
                object timing=Field(hit,"_timing");float windup=(float)Field(timing,"Windup"),swing=(float)Field(timing,"Swing");
                float distance=(float)Field(hit,"_motionDistance");
                Vector3 mount=weapon.transform.position;
                Time.timeScale=0;
                for(int i=0;i<=40;i++)
                {
                    float time=windup+swing*i/40f;
                    Call(hit,"ApplyMotionPose",time,mount,side==1?0f:180f);
                    Vector3 grip=hit.transform.position-mount;
                    var visual=(SpriteRenderer)Property(hit,"VisualRenderer");
                    Assert.AreEqual(side<0,visual.flipY);
                    if(side==1)right[i]=grip;
                    else
                    {
                        Assert.That(grip.x,Is.EqualTo(-right[i].x).Within(.0001));
                        Assert.That(grip.y,Is.EqualTo(right[i].y).Within(.0001));
                    }
                    if(i==0)Assert.That(grip.y,Is.EqualTo(distance*.5f).Within(.0001));
                    if(i==20)Assert.That(grip.x*side,Is.EqualTo(distance*.75f).Within(.0001));
                    if(i==40)Assert.That(grip.y,Is.EqualTo(-distance*.5f).Within(.0001));
                }
                Call(hit,"Cancel");Time.timeScale=1;
                Call(_loadout,"RemoveRoundWeapon",weapon);
            }
        }

        /// <summary>零范围加成时，各方向敌人进入完整基础距离都能命中，背侧挂点不会缩短索敌距离。</summary>
        [UnityTest] public IEnumerator Melee_BaseReachHitsAllDirections_WithoutRangeBonus()
        {
            object aim=_player.GetComponent(T("AimController"));Call(aim,"SetMode",Enum.Parse(T("AimController+AimMode"),"NearestEnemy"));
            foreach(string id in new[]{"01_copper_rapier","02_saw_cleaver","03_tide_spear","11_piston_gauntlet","19_echo_dagger"})
            foreach(Vector3 direction in new[]{Vector3.right,Vector3.left,Vector3.up,Vector3.down})
            {
                Component weapon=Equip(id); float range=(float)Property(weapon,"CurrentAttackRange");
                Assert.That(range,Is.GreaterThan(1f),id+" 无范围加成仍有基础距离");
                Vector3 origin=(Vector2)Property(weapon,"AttackOrigin");
                Property(_round,"Current").GetType().GetProperty("Elapsed").SetValue(Property(_round,"Current"),0f);
                Component target=Enemy(origin+direction*(float)Property(weapon,"CurrentVisualRange")*.95f);Physics2D.SyncTransforms();
                yield return new WaitForSeconds(.06f);
                Component hit=FindAttack("MeleeSwingHitbox",_data[id]);Assert.NotNull(hit,id);
                Set(weapon,"_currentCooldown",99f);
                yield return new WaitForSeconds((float)Field(hit,"_duration")+.04f);
                Assert.That((float)Property(target,"CurrentHealth"),Is.LessThan(100f),id+" "+direction);
                target.gameObject.SetActive(false);Call(_loadout,"RemoveRoundWeapon",weapon);
            }
        }

        /// <summary>自动动作跟踪原目标且随玩家移动；回收禁伤，目标退出后不换敌。</summary>
        [UnityTest] public IEnumerator Melee_AutoTrackingAndMovingMount_RecoveryDoesNotDamage()
        {
            Component weapon=Equip("02_saw_cleaver");Set(weapon,"_currentCooldown",99f);
            Vector3 mount=weapon.transform.position;
            Component target=Enemy(mount+Vector3.right*1.2f);Physics2D.SyncTransforms();
            yield return new WaitForSeconds(.2f);
            Call(weapon,"Attack");Component hit=FindAttack("MeleeSwingHitbox",_data["02_saw_cleaver"]);
            var held=(SpriteRenderer)Property(weapon.GetComponent(T("WeaponHeldView")),"Renderer");
            Assert.That(Vector3.Distance(held.transform.position,((SpriteRenderer)Property(hit,"VisualRenderer")).transform.position),Is.LessThan(.001));
            target.transform.position=weapon.transform.position+new Vector3(.8f,.8f);
            _player.transform.position+=Vector3.left*.3f;Physics2D.SyncTransforms();
            yield return null;yield return null;
            Vector2 delta=target.transform.position-weapon.transform.position;
            float expected=Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg;
            Assert.That(Mathf.DeltaAngle((float)Field(hit,"_centerAngle"),expected),Is.EqualTo(0).Within(.01));
            Assert.AreEqual(weapon.transform.position,Property(hit,"AttackPivot"));
            target.transform.position=weapon.transform.position+Vector3.up*20;Physics2D.SyncTransforms();
            yield return null;yield return null;
            float frozen=(float)Field(hit,"_centerAngle");
            target.transform.position=weapon.transform.position+Vector3.left;Physics2D.SyncTransforms();
            yield return null;
            Assert.AreEqual(frozen,Field(hit,"_centerAngle"),"退出检测范围后本击不得重新绑定目标");
            object timing=Field(hit,"_timing");
            float activeEnd=(float)Field(timing,"Windup")+(float)Field(timing,"Swing");
            while(hit.gameObject.activeInHierarchy&&(float)Field(hit,"_elapsedTime")<=activeEnd)yield return null;
            Assert.IsTrue(hit.gameObject.activeInHierarchy);
            Set(target,"_currentHealth",100f);
            target.transform.position=((SpriteRenderer)Property(hit,"VisualRenderer")).transform.position;Physics2D.SyncTransforms();
            yield return new WaitForSeconds((float)Field(timing,"Recovery")+.05f);
            Assert.AreEqual(100f,Property(target,"CurrentHealth"));Assert.IsTrue(held.enabled);
        }

        /// <summary>高攻速跨帧扫过目标仍准确命中一次；重置装备取消动作且不会产生延迟攻击。</summary>
        [UnityTest] public IEnumerator Melee_FastWholeSwingSamplingAndCancellation_AreSafe()
        {
            Component weapon=Equip("02_saw_cleaver");Set(weapon,"_currentCooldown",99f);
            Component target=Enemy(_player.transform.position+Vector3.up*1.2f);Physics2D.SyncTransforms();
            Call(weapon,"Attack");Component hit=FindAttack("MeleeSwingHitbox",_data["02_saw_cleaver"]);
            object timing=Property(weapon,"CurrentMeleeTiming");
            Set(hit,"_elapsedTime",(float)Field(timing,"Windup")+(float)Field(timing,"Swing")+.001f);
            Call(hit,"UpdateTargeted",0f);
            float damage=(float)Call(weapon,"GetCurrentDamage");Assert.AreEqual(100f-damage,Property(target,"CurrentHealth"));
            Call(hit,"UpdateTargeted",0f);Assert.AreEqual(100f-damage,Property(target,"CurrentHealth"));
            Call(weapon,"ResetRoundCooldown");Set(weapon,"_currentCooldown",99f);Assert.IsFalse(hit.gameObject.activeInHierarchy);
            Call(weapon,"Attack");hit=FindAttack("MeleeSwingHitbox",_data["02_saw_cleaver"]);
            Call(_loadout,"RemoveRoundWeapon",weapon);yield return null;Assert.IsFalse(hit.gameObject.activeInHierarchy);
        }

        /// <summary>实际自动攻击中冻结冷却，完整动作结束之后才等待已抽样的下一击冷却。</summary>
        [UnityTest] public IEnumerator Melee_CooldownStartsAfterRecovery_NotAtLaunch()
        {
            Component weapon=Equip("02_saw_cleaver");
            Component target=Enemy(weapon.transform.position+Vector3.right*1.2f);Set(target,"_currentHealth",1000f);
            Physics2D.SyncTransforms();
            Component hit=null;
            for(int i=0;i<40&&hit==null;i++){yield return null;hit=FindAttack("MeleeSwingHitbox",_data["02_saw_cleaver"]);}
            Assert.NotNull(hit);
            float pending=(float)Field(weapon,"_currentCooldown");
            Assert.Greater(pending,0f);
            yield return new WaitForSeconds(.2f);
            Assert.IsTrue(hit.gameObject.activeInHierarchy);
            Assert.AreEqual(pending,Field(weapon,"_currentCooldown"),"动作期间冷却不能偷跑");
            while(hit.gameObject.activeInHierarchy)yield return null;
            float finished=Time.time;
            Assert.That((float)Field(weapon,"_currentCooldown"),Is.GreaterThan(pending-.05f));
            yield return new WaitForSeconds(Mathf.Max(.01f,pending-.1f));
            Assert.IsNull(FindAttack("MeleeSwingHitbox",_data["02_saw_cleaver"]));
            float timeout=Time.time+1f;
            while(FindAttack("MeleeSwingHitbox",_data["02_saw_cleaver"])==null&&Time.time<timeout)yield return null;
            Assert.NotNull(FindAttack("MeleeSwingHitbox",_data["02_saw_cleaver"]));
            Assert.That(Time.time-finished,Is.InRange(pending-.05f,pending+.1f));
        }

        /// <summary>手动模式允许空场出手，动作中方向冻结；自动目标跨生命代次后不再追踪复用对象。</summary>
        [UnityTest] public IEnumerator Melee_ManualDirectionAndPooledTargetIdentity_AreStable()
        {
            object aim=_player.GetComponent(T("AimController"));
            Call(aim,"SetMode",Enum.Parse(T("AimController+AimMode"),"Manual"));Call(aim,"SetManualDirection",Vector2.right);
            Component weapon=Equip("02_saw_cleaver");
            yield return new WaitForSeconds(.04f);
            Component hit=FindAttack("MeleeSwingHitbox",_data["02_saw_cleaver"]);Assert.NotNull(hit,"手动空场仍可出手");
            Set(weapon,"_currentCooldown",99f);
            Call(aim,"SetManualDirection",Vector2.left);yield return null;
            Assert.That((float)Field(hit,"_centerAngle"),Is.EqualTo(0).Within(.001));
            Call(hit,"Cancel");
            Call(aim,"SetMode",Enum.Parse(T("AimController+AimMode"),"NearestEnemy"));
            Component target=Enemy(weapon.transform.position+Vector3.right*1.1f);Physics2D.SyncTransforms();
            Call(weapon,"Attack");hit=FindAttack("MeleeSwingHitbox",_data["02_saw_cleaver"]);
            uint generation=(uint)Property(target,"LifeGeneration");
            target.gameObject.SetActive(false);target.transform.position=weapon.transform.position+Vector3.up;
            target.gameObject.SetActive(true);Set(target,"_currentHealth",100f);Physics2D.SyncTransforms();
            Assert.AreNotEqual(generation,Property(target,"LifeGeneration"));
            yield return null;yield return null;
            Assert.That((float)Field(hit,"_centerAngle"),Is.EqualTo(0).Within(.001),"复用目标不能牵引上一生命的攻击");
        }

        /// <summary>暂停时空闲持武图也冻结镜像和位置；恢复后再接收新手动方向。</summary>
        [UnityTest] public IEnumerator Melee_IdlePause_FreezesMirrorAndPose()
        {
            object aim=_player.GetComponent(T("AimController"));
            Call(aim,"SetMode",Enum.Parse(T("AimController+AimMode"),"Manual"));Call(aim,"SetManualDirection",Vector2.right);
            Component weapon=Equip("02_saw_cleaver");Set(weapon,"_currentCooldown",99f);
            yield return new WaitForSeconds(.2f);
            var held=(SpriteRenderer)Property(weapon.GetComponent(T("WeaponHeldView")),"Renderer");
            Vector3 position=held.transform.position;Quaternion rotation=held.transform.rotation;
            Time.timeScale=0;Call(aim,"SetManualDirection",Vector2.left);
            Call(weapon.GetComponent(T("WeaponHeldView")),"LateUpdate");
            yield return null;yield return null;
            Assert.IsFalse(held.flipY);Assert.AreEqual(position,held.transform.position);Assert.AreEqual(rotation,held.transform.rotation);
            Time.timeScale=1;yield return null;yield return null;
            Assert.IsTrue(held.flipY);
        }

        /// <summary>补采包含挂点移动；大幅瞬移取消攻击，不在玩家移动连线上误伤。</summary>
        [UnityTest] public IEnumerator Melee_MovingMountSweepsDamage_AndTeleportCancels()
        {
            object aim=_player.GetComponent(T("AimController"));
            Call(aim,"SetMode",Enum.Parse(T("AimController+AimMode"),"Manual"));Call(aim,"SetManualDirection",Vector2.right);
            Component weapon=Equip("02_saw_cleaver");Set(weapon,"_currentCooldown",99f);
            yield return new WaitForSeconds(.2f);
            Call(weapon,"Attack");Component hit=FindAttack("MeleeSwingHitbox",_data["02_saw_cleaver"]);
            object timing=Field(hit,"_timing");float windup=(float)Field(timing,"Windup"),swing=(float)Field(timing,"Swing");
            float at=windup+swing*.5f;Set(hit,"_elapsedTime",at);Call(hit,"UpdateTargeted",0f);
            var collider=hit.GetComponent<CapsuleCollider2D>();
            Vector3 center=hit.transform.TransformPoint(collider.offset);
            Component target=Enemy(center+Vector3.up*.5f);Physics2D.SyncTransforms();
            _player.transform.position+=Vector3.up;Physics2D.SyncTransforms();
            Set(hit,"_elapsedTime",at+swing*.01f);Call(hit,"UpdateTargeted",at);
            Assert.That((float)Property(target,"CurrentHealth"),Is.EqualTo(100f-(float)Call(weapon,"GetCurrentDamage")).Within(.001));
            Component untouched=Enemy(center+Vector3.right*5);
            _player.transform.position+=Vector3.right*10;Physics2D.SyncTransforms();
            Set(hit,"_elapsedTime",at+swing*.02f);Call(hit,"UpdateTargeted",at+swing*.01f);
            Assert.IsFalse(hit.gameObject.activeInHierarchy);Assert.AreEqual(100f,Property(untouched,"CurrentHealth"));
            yield return null;
        }

        /// <summary>鞭子保留资产但正式购买与调试授予拒绝创建，避免僵硬素材重新进入局内。</summary>
        [UnityTest] public IEnumerator Whip_IsUnavailableUntilAnimationExists()
        {
            object data=_data["17_brand_whip"];Assert.IsTrue((bool)Field(data,"retiredFromPool"));
            for(int tier=1;tier<=4;tier++)
            {Assert.IsFalse((bool)Call(_loadout,"CanBuyRoundWeapon",data,tier));Assert.IsNull(Call(_loadout,"BuyRoundWeapon",data,tier));}
            Assert.IsNull(Call(_loadout,"DebugEnsureWeaponLevel",data,1));
            Component panel=(Component)UnityEngine.Object.FindObjectOfType(T("WeaponDebugPanel"));
            if(panel==null){var go=new GameObject("WhipDebugFixture");_fixtures.Add(go);panel=go.AddComponent(T("WeaponDebugPanel"));}
            Call(panel,"ResolveLevelUpManager");Call(panel,"RefreshWeaponList");
            foreach(object upgrade in (IEnumerable)Field(panel,"_weaponUpgrades"))Assert.AreNotSame(data,Field(upgrade,"weaponToGrant"));
            yield return null;
        }

        /// <summary>所有近战八方向采用同一局部 +X 攻击轴；刀身镜像与碰撞中心在完整动作中对应。</summary>
        [UnityTest] public IEnumerator Melee_EightDirections_LocalMotionAndBladeRemainAligned()
        {
            object aim=_player.GetComponent(T("AimController"));Call(aim,"SetMode",Enum.Parse(T("AimController+AimMode"),"Manual"));
            foreach(string id in new[]{"01_copper_rapier","02_saw_cleaver","03_tide_spear","11_piston_gauntlet","19_echo_dagger"})
            for(int i=0;i<8;i++)
            {
                object runtime=Property(_round,"Current");runtime.GetType().GetProperty("Elapsed").SetValue(runtime,0f);
                Vector3 direction=Quaternion.Euler(0,0,i*45f)*Vector3.right;
                Call(aim,"SetManualDirection",(Vector2)direction);
                Component weapon=Equip(id);Set(weapon,"_currentCooldown",99f);
                if(id=="03_tide_spear")Set(weapon,"_nextThrust",false);
                Vector3 origin=weapon.transform.position;
                Component target=Enemy(origin+direction*(float)Property(weapon,"CurrentVisualRange")*.85f);
                Physics2D.SyncTransforms();yield return new WaitForSeconds(.15f);
                Call(weapon,"Attack");Component hit=FindAttack("MeleeSwingHitbox",_data[id]);Assert.NotNull(hit);
                bool thrust=(bool)Field(hit,"_thrust");object timing=Field(hit,"_timing");
                float at=(float)Field(timing,"Windup")+(float)Field(timing,"Swing")*(thrust?1f:.5f);
                Time.timeScale=0;Call(hit,"ApplyMotionPose",at,origin,(float)Field(hit,"_centerAngle"));
                var visual=(SpriteRenderer)Property(hit,"VisualRenderer");
                float correction=(float)Field(_data[id],"visualAngleOffset")*(visual.flipY?-1f:1f);
                Vector3 axis=visual.transform.TransformDirection(Quaternion.Euler(0,0,-correction)*Vector3.right).normalized;
                Assert.That(Vector3.Dot(axis,direction),Is.GreaterThan(.999f),id);
                float travel=(float)Field(hit,"_motionDistance")*(thrust?1f:.75f);
                Assert.That(Vector3.Dot(hit.transform.position-origin,direction),Is.EqualTo(travel).Within(.001));
                var collider=hit.GetComponent<CapsuleCollider2D>();
                Assert.That(Vector3.Distance(hit.transform.TransformPoint(collider.offset),visual.transform.position),Is.LessThan(.002));
                if(id=="03_tide_spear")yield return Capture("direction-spear-"+i);
                // 恢复真实时钟并从此前初始时间连续推进，以实际命中验证整个路径。
                Time.timeScale=1;
                yield return new WaitForSeconds((float)Field(hit,"_duration")+.03f);
                Assert.Less((float)Property(target,"CurrentHealth"),100f,id+" 实际路径必须命中");
                target.gameObject.SetActive(false);Call(_loadout,"RemoveRoundWeapon",weapon);
            }
        }

        /// <summary>真实角色属性接入动作时序，范围提升不再倍乘整个冷却，详情与运行时一致。</summary>
        [UnityTest] public IEnumerator Melee_LiveStatsControlMotionAndTooltip()
        {
            Component weapon=Equip("02_saw_cleaver");Set(weapon,"_currentCooldown",99f);
            object baseline=Property(weapon,"CurrentMeleeTiming");float range=(float)Property(weapon,"CurrentAttackRange");
            object stats=_player.GetComponent(T("PlayerStats"));
            Array modifiers=Array.CreateInstance(T("PlayerStatModifier"),2);
            modifiers.SetValue(Activator.CreateInstance(T("PlayerStatModifier"),Enum.Parse(T("PlayerStatType"),"AttackSpeed"),Enum.Parse(T("PlayerStatModifierMode"),"Flat"),100f),0);
            modifiers.SetValue(Activator.CreateInstance(T("PlayerStatModifier"),Enum.Parse(T("PlayerStatType"),"Range"),Enum.Parse(T("PlayerStatModifierMode"),"Flat"),200f),1);
            Call(stats,"SetModifiers","melee.revision.test",modifiers);
            object changed=Property(weapon,"CurrentMeleeTiming");
            Assert.That((float)Property(weapon,"CurrentAttackRange"),Is.EqualTo(range+1f).Within(.001));
            Assert.That((float)Field(changed,"Swing"),Is.EqualTo(.238839286f).Within(.00001));
            Assert.That((float)Field(changed,"Recovery"),Is.EqualTo(.05f).Within(.00001));
            float interval=(float)Call(weapon,"GetCurrentCooldown");
            string details=(string)T("RoundShopPresentation").GetMethod("WeaponDetails").Invoke(null,new[]{_data["02_saw_cleaver"],(object)1,stats,false});
            StringAssert.Contains(interval.ToString("0.##")+"秒",details);
            Component target=Enemy(_player.transform.position+Vector3.up*2);Physics2D.SyncTransforms();yield return new WaitForSeconds(.2f);
            Call(weapon,"Attack");Component hit=FindAttack("MeleeSwingHitbox",_data["02_saw_cleaver"]);
            Assert.That((float)Field(hit,"_duration"),Is.EqualTo((float)Property(changed,"Total")).Within(.001));
            var held=(SpriteRenderer)Property(weapon.GetComponent(T("WeaponHeldView")),"Renderer");
            var visual=(SpriteRenderer)Property(hit,"VisualRenderer");Assert.AreEqual(held.sortingLayerID,visual.sortingLayerID);Assert.AreEqual(held.sortingOrder,visual.sortingOrder);
            yield return new WaitForSeconds((float)Field(hit,"_duration")+.03f);Assert.Less((float)Property(target,"CurrentHealth"),100f);
        }

        /// <summary>真实暂停菜单冻结原动作，恢复后继续命中；商店阶段立即取消而不产生残留伤害。</summary>
        [UnityTest] public IEnumerator Melee_ManualPausePreservesSwing_IntermissionCancelsIt()
        {
            Component weapon=Equip("02_saw_cleaver");Set(weapon,"_currentCooldown",99f);
            Component target=Enemy(_player.transform.position+Vector3.up*1.2f);Physics2D.SyncTransforms();
            Call(weapon,"Attack");Component hit=FindAttack("MeleeSwingHitbox",_data["02_saw_cleaver"]);
            yield return null;
            object flow=UnityEngine.Object.FindObjectOfType(T("GameFlowManager"));
            float elapsed=(float)Field(hit,"_elapsedTime");Vector3 position=hit.transform.position;
            Time.timeScale=0;Call(hit,"Update");Assert.AreEqual(elapsed,Field(hit,"_elapsedTime"),"暂停当帧不能消费旧 deltaTime");Time.timeScale=1;
            Call(flow,"PauseGame");
            try
            {
                Assert.IsTrue((bool)Property(flow,"IsManuallyPaused"));
                yield return new WaitForSecondsRealtime(.12f);
                Assert.IsTrue(hit.gameObject.activeInHierarchy);Assert.AreEqual(elapsed,Field(hit,"_elapsedTime"));Assert.AreEqual(position,hit.transform.position);
                Assert.AreEqual(100f,Property(target,"CurrentHealth"));
            }
            finally {Call(flow,"ResumeGame");}
            yield return new WaitForSeconds((float)Field(hit,"_duration")+.02f);
            Assert.Less((float)Property(target,"CurrentHealth"),100f);
            Call(weapon,"Attack");hit=FindAttack("MeleeSwingHitbox",_data["02_saw_cleaver"]);float health=(float)Property(target,"CurrentHealth");
            _round.GetType().GetProperty("Phase").SetValue(_round,Enum.Parse(T("RoundPhase"),"Shop"));
            yield return null;yield return null;Assert.IsFalse(hit.gameObject.activeInHierarchy);Assert.AreEqual(health,Property(target,"CurrentHealth"));
        }

        /// <summary>以真实时间推进左右完整动作，记录连续画面与逐帧姿势，不用单个中点代替视觉证据。</summary>
        [UnityTest] public IEnumerator Melee_VisibleSequence_RecordsBothSidesAndHandoff()
        {
            object aim=_player.GetComponent(T("AimController"));
            Call(aim,"SetMode",Enum.Parse(T("AimController+AimMode"),"Manual"));
            foreach(int side in new[]{1,-1})
            {
                object runtime=Property(_round,"Current");runtime.GetType().GetProperty("Elapsed").SetValue(runtime,0f);
                Call(aim,"SetManualDirection",Vector2.right*side);
                Component weapon=Equip("02_saw_cleaver");Set(weapon,"_currentCooldown",99f);
                Component target=Enemy(weapon.transform.position+Vector3.right*(1.25f*side));
                SpriteRenderer targetView=target.gameObject.AddComponent<SpriteRenderer>();
                SpriteRenderer playerView=_player.GetComponentInChildren<SpriteRenderer>();
                targetView.sprite=playerView.sprite;targetView.color=new Color(1f,.35f,.35f);
                targetView.sortingLayerID=playerView.sortingLayerID;targetView.sortingOrder=playerView.sortingOrder;
                targetView.transform.localScale=Vector3.one*.5f;Physics2D.SyncTransforms();
                string label=side>0?"right":"left";
                yield return new WaitForSeconds(.3f);yield return Capture("sequence-"+label+"-00-held");
                Time.timeScale=.2f;Call(weapon,"Attack");Component hit=FindAttack("MeleeSwingHitbox",_data["02_saw_cleaver"]);
                float total=(float)Field(hit,"_duration");
                string folder=Path.GetFullPath("Logs/Session27/OriginalMelee");Directory.CreateDirectory(folder);
                var poses=new System.Text.StringBuilder("elapsed,gripX,gripY,rotation,flipY,held,active\n");
                var view=(SpriteRenderer)Property(weapon.GetComponent(T("WeaponHeldView")),"Renderer");
                var attack=(SpriteRenderer)Property(hit,"VisualRenderer");
                for(int sample=1;sample<=18;sample++)
                {
                    float at=total*sample/18f;
                    while(hit.gameObject.activeInHierarchy&&(float)Field(hit,"_elapsedTime")<at)
                    {
                        poses.AppendLine(Field(hit,"_elapsedTime")+","+hit.transform.position.x+","+hit.transform.position.y+","+hit.transform.eulerAngles.z+","+attack.flipY+","+view.enabled+","+Property(hit,"IsStriking"));
                        yield return null;
                    }
                    yield return Capture("sequence-"+label+"-"+sample.ToString("00"));
                }
                File.WriteAllText(Path.Combine(folder,"trajectory-"+label+".csv"),poses.ToString());
                Assert.Less((float)Property(target,"CurrentHealth"),100f);
                Assert.IsTrue(view.enabled,"回收完成必须交还持武图");
                Time.timeScale=1f;target.gameObject.SetActive(false);Call(_loadout,"RemoveRoundWeapon",weapon);
            }
        }

        /// <summary>比较恢复后的真实视野与上一版近景，确保镜头拉远且角色世界尺寸不变。</summary>
        [UnityTest] public IEnumerator Camera_RestoredPixelPerfectView_WidensViewWithoutChangingWorldScale()
        {
            Equip("03_tide_spear");Equip("07_medical_lancet");
            Component vcam=null, pixel=null;
            foreach(var component in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
            { if(component.GetType().Name=="CinemachineVirtualCamera")vcam=component;if(component.GetType().Name=="PixelPerfectCamera")pixel=component; }
            Assert.NotNull(vcam);Assert.NotNull(pixel);
            object lens=Field(vcam,"m_Lens");Type lensType=lens.GetType();
            float intended=(float)lensType.GetField("OrthographicSize").GetValue(lens);
            Assert.That(intended,Is.EqualTo(5f).Within(.001));
            Vector3 playerScale=_player.transform.localScale;
            Assert.AreEqual(480,pixel.GetType().GetProperty("refResolutionX").GetValue(pixel));
            Assert.AreEqual(270,pixel.GetType().GetProperty("refResolutionY").GetValue(pixel));
            // 无图形批处理不会驱动像素相机的渲染回调，只验证正式配置；实际视野比例由图形专项验证。
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            yield return new WaitForSeconds(.2f);
            float accepted=Camera.main.orthographicSize;
            yield return CaptureGame("framing-accepted");
            try
            {
                lensType.GetField("OrthographicSize").SetValue(lens,5f/1.5f);Set(vcam,"m_Lens",lens);
                pixel.GetType().GetProperty("refResolutionX").SetValue(pixel,320);pixel.GetType().GetProperty("refResolutionY").SetValue(pixel,180);
                yield return new WaitForSeconds(.2f);
                float close=Camera.main.orthographicSize;
                float ratio=accepted/close;
                // 像素相机按整数倍对齐，720p 和 1080p 的视野增幅可能分别接近 1.5 和 1.67。
                Assert.That(ratio,Is.InRange(1.25f,1.8f));
                yield return CaptureGame("framing-close");
                string directory=Path.GetFullPath("Logs/Session26/FramingAccepted");Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory,"framing-measurement.txt"),$"Screen={Screen.width}x{Screen.height}; acceptedOrtho={accepted}; previousCloseOrtho={close}; visibleSpanRatio={ratio}; playerScale={playerScale}");
            }
            finally
            {
                lensType.GetField("OrthographicSize").SetValue(lens,intended);Set(vcam,"m_Lens",lens);
                pixel.GetType().GetProperty("refResolutionX").SetValue(pixel,480);pixel.GetType().GetProperty("refResolutionY").SetValue(pixel,270);
            }
            Assert.AreEqual(playerScale,_player.transform.localScale);
        }

        /// <summary>图形环境捕获含 HUD 的真实 GameView，避免手动 Render 引发像素相机重新取整。</summary>
        private static IEnumerator CaptureGame(string name)
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null || Application.isBatchMode)yield break;
            string directory=Path.GetFullPath("Logs/Session26/FramingAccepted");Directory.CreateDirectory(directory);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,name+".png"));
            yield return null;yield return null;
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
            string directory=Path.GetFullPath("Logs/Session27/OriginalMelee/graphics"); Directory.CreateDirectory(directory);
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
