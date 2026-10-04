using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>由原版已核实样本提供数值和几何预期，避免只按实现自身重复推导。</summary>
public sealed class OriginalMeleeTests
{
    /// <summary>白色 Chopper 样本：动作与冷却相加；增加范围只延长主动段。</summary>
    [Test] public void Timing_MatchesRecoveredReference_AndSeparatesCooldown()
    {
        var baseMotion = new MeleeAttackTiming(.1f, 1.35f, 1f);
        var wide = new MeleeAttackTiming(.1f, 1.85f, 1f);
        Assert.That(baseMotion.Windup, Is.EqualTo(.1f).Within(.000001));
        Assert.That(baseMotion.Swing, Is.EqualTo(.244642857f).Within(.000001));
        Assert.That(baseMotion.Recovery, Is.EqualTo(.2f).Within(.000001));
        Assert.That(wide.Swing - baseMotion.Swing, Is.EqualTo(.053571429f).Within(.000001));
        Assert.AreEqual(baseMotion.Windup, wide.Windup);
        Assert.AreEqual(baseMotion.Recovery, wide.Recovery);
        Assert.That(MeleeAttackTiming.Interval(new WeaponLevelData { cooldown=.45f }, 1.35f, 1f),
            Is.EqualTo(.994642857f).Within(.000001));
    }

    /// <summary>正负攻速并非把整段动作统一缩放；冷却保留 tick 截断和最低值。</summary>
    [Test] public void Speed_PositiveAndNegativeBranches_HaveDifferentMotionEffects()
    {
        var fast = new MeleeAttackTiming(.1f, 1.35f, .5f);
        var slow = new MeleeAttackTiming(.1f, 1.35f, 1.5f);
        Assert.That(fast.Windup, Is.EqualTo(.05f).Within(.000001));
        Assert.That(fast.Recovery, Is.EqualTo(.05f).Within(.000001));
        Assert.That(fast.Swing, Is.EqualTo(.158482143f).Within(.000001));
        Assert.That(slow.Windup, Is.EqualTo(.1f).Within(.000001));
        Assert.That(slow.Recovery, Is.EqualTo(.2f).Within(.000001));
        Assert.That(slow.Swing, Is.EqualTo(.269642857f).Within(.000001));
        Assert.That(MeleeAttackTiming.Cooldown(.45f,.5f), Is.EqualTo(13f/60f).Within(.000001));
        Assert.That(MeleeAttackTiming.Cooldown(.45f,1.5f), Is.EqualTo(40f/60f).Within(.000001));
        Assert.That(MeleeAttackTiming.Cooldown(.45f,.00001f), Is.EqualTo(2f/60f).Within(.000001));
        Assert.AreEqual(.25f, MeleeAttackTiming.Recoil(.25f,1.5f));
        Assert.AreEqual(.125f, MeleeAttackTiming.Recoil(.25f,.5f));
    }

    /// <summary>冷却错峰使用持武数量和给定随机样本，不让面板参考间隔随机漂移。</summary>
    [Test] public void CooldownJitter_HasVerifiedBounds()
    {
        Assert.That(MeleeAttackTiming.RandomizedCooldown(.45f,1,0), Is.EqualTo(22f/60f).Within(.000001));
        Assert.That(MeleeAttackTiming.RandomizedCooldown(.45f,1,1), Is.EqualTo(32f/60f).Within(.000001));
        Assert.That(MeleeAttackTiming.RandomizedCooldown(.45f,6,0), Is.EqualTo(1f/60f).Within(.000001));
        Assert.That(MeleeAttackTiming.RandomizedCooldown(.45f,60,1), Is.EqualTo(57f/60f).Within(.000001));
    }

    /// <summary>左右两条完整轨迹在世界空间水平镜像；总主动角为 324 度，回收准确归零。</summary>
    [Test] public void Sweep_TranslationAndRotation_MirrorAcrossThePlayer()
    {
        var timing = new MeleeAttackTiming(.1f,1.35f,1f);
        for(int i=0;i<=120;i++)
        {
            float time=timing.Total*i/120f;
            var right=MeleeAttackMotion.Evaluate(false,true,1.35f,.25f,timing,time,default);
            var left=MeleeAttackMotion.Evaluate(false,false,1.35f,.25f,timing,time,default);
            Vector2 worldLeft=Quaternion.Euler(0,0,180)*(Vector3)left.Position;
            Assert.That(worldLeft.x, Is.EqualTo(-right.Position.x).Within(.00001));
            Assert.That(worldLeft.y, Is.EqualTo(right.Position.y).Within(.00001));
            Assert.That(left.Angle, Is.EqualTo(-right.Angle).Within(.00001));
        }
        var start=MeleeAttackMotion.Evaluate(false,true,1.35f,.25f,timing,timing.Windup,default);
        var middle=MeleeAttackMotion.Evaluate(false,true,1.35f,.25f,timing,timing.Windup+timing.Swing*.5f,default);
        var end=MeleeAttackMotion.Evaluate(false,true,1.35f,.25f,timing,timing.Windup+timing.Swing,default);
        Assert.That(start.Position.x,Is.EqualTo(-.25f).Within(.00001));
        Assert.That(start.Position.y,Is.EqualTo(.675f).Within(.00001));
        Assert.That(middle.Position.x,Is.EqualTo(1.0125f).Within(.00001));
        Assert.That(middle.Angle,Is.EqualTo(0f).Within(.00001));
        Assert.That(start.Angle-end.Angle,Is.EqualTo(324f).Within(.0001));
        var rest=MeleeAttackMotion.Evaluate(false,true,1.35f,.25f,timing,timing.Total+.00001f,default);
        Assert.AreEqual(Vector2.zero,rest.Position);Assert.AreEqual(0f,rest.Angle);
    }

    /// <summary>突刺走完整距离；横挥仅在大范围和近目标时缩短，垂直边界判侧固定。</summary>
    [Test] public void Thrust_UsesFullTravel_AndSweepUsesItsMinimumDistance()
    {
        Assert.AreEqual(1.35f,MeleeAttackMotion.Distance(false,1.35f,.1f));
        Assert.AreEqual(2.5f,MeleeAttackMotion.Distance(false,4f,1f));
        Assert.AreEqual(3f,MeleeAttackMotion.Distance(false,4f,3f));
        Assert.AreEqual(4f,MeleeAttackMotion.Distance(true,4f,1f));
        var timing=new MeleeAttackTiming(.1f,2.25f,1f);
        var end=MeleeAttackMotion.Evaluate(true,true,2.25f,.25f,timing,timing.Windup+timing.Swing,default);
        Assert.That(end.Position.x,Is.EqualTo(2.25f).Within(.00001));
        Assert.AreEqual(0f,end.Position.y);Assert.AreEqual(0f,end.Angle);
        Assert.IsFalse(MeleeAttackMotion.FacesRight(90));
        Assert.IsFalse(MeleeAttackMotion.FacesRight(-90));
        Assert.IsTrue(MeleeAttackMotion.FacesRight(89.99f));
        Assert.IsFalse(MeleeAttackMotion.FacesRight(90.01f));
    }

    /// <summary>正式近战资产具有独立固定几何和前摇配置，全部档位保持原有攻击类别。</summary>
    [Test] public void MeleeAssets_HaveExplicitPositiveGeometry()
    {
        int count=0;
        foreach(string guid in AssetDatabase.FindAssets("t:WeaponDataSO",new[]{"Assets/Data"}))
        {
            var data=AssetDatabase.LoadAssetAtPath<WeaponDataSO>(AssetDatabase.GUIDToAssetPath(guid));
            if(data.runtimeType!=WeaponRuntimeType.Melee)continue;
            count++;
            Assert.Greater(data.heldSize,.05f,data.name);
            Assert.Greater(data.meleeHitWidth,0f,data.name);
            Assert.LessOrEqual(data.meleeHitWidth,data.heldSize,data.name);
            Assert.Greater(data.meleeWindup,0f,data.name);
            Assert.GreaterOrEqual(data.meleeRecoil,0f,data.name);
        }
        Assert.GreaterOrEqual(count,7);
    }
}
