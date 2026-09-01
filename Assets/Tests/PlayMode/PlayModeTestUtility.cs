using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>
    /// 为 PlayMode 冒烟测试统一管理临时对象与全局时间状态。
    /// 所有对象都在真实 Player Loop 中启用和销毁，避免测试之间残留场景状态。
    /// </summary>
    public abstract class PlayModeComponentTestBase
    {
        private List<UnityEngine.Object> _trackedObjects;

        /// <summary>在每项测试前恢复正常时间流速，并建立临时对象登记表。</summary>
        [SetUp]
        public void PreparePlayModeTest()
        {
            Time.timeScale = 1f;
            _trackedObjects = new List<UnityEngine.Object>();
        }

        /// <summary>销毁全部临时对象，并等待一帧让 Unity 完成 OnDisable 与 OnDestroy。</summary>
        [UnityTearDown]
        public IEnumerator CleanUpPlayModeTest()
        {
            Time.timeScale = 1f;

            for (int index = _trackedObjects.Count - 1; index >= 0; index--)
            {
                UnityEngine.Object trackedObject = _trackedObjects[index];
                if (trackedObject != null)
                {
                    UnityEngine.Object.Destroy(trackedObject);
                }
            }

            _trackedObjects.Clear();
            yield return null;
            Time.timeScale = 1f;
        }

        /// <summary>创建并登记一个测试 GameObject，可选择在配置完成前保持禁用。</summary>
        protected GameObject CreateTrackedGameObject(string name, bool active = true)
        {
            GameObject createdObject = new GameObject(name);
            if (!active)
            {
                createdObject.SetActive(false);
            }

            TrackObject(createdObject);
            return createdObject;
        }

        /// <summary>登记由生产代码生成的对象，保证测试结束后能够完整清理。</summary>
        protected T TrackObject<T>(T target) where T : UnityEngine.Object
        {
            if (target != null && !_trackedObjects.Contains(target))
            {
                _trackedObjects.Add(target);
            }

            return target;
        }

        /// <summary>取得项目必需 Layer；若配置丢失则给出可定位的失败信息。</summary>
        protected static int RequireLayer(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            Assert.That(layer, Is.GreaterThanOrEqualTo(0), $"项目缺少必需 Layer：{layerName}");
            return layer;
        }
    }

    /// <summary>
    /// 从 PlayMode 测试程序集访问默认 Assembly-CSharp 中的生产类型。
    /// 项目尚未迁移运行时 asmdef，因此使用严格反射避免为了测试重排全部脚本程序集。
    /// 类型、字段或方法改名时会立即抛出异常，不会让测试静默失效。
    /// </summary>
    internal static class RuntimeComponentTestUtility
    {
        private const string RuntimeAssemblyName = "Assembly-CSharp";
        private const BindingFlags AllInstanceMembers =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AllStaticMembers =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>在默认运行时程序集中取得必需类型。</summary>
        public static Type RequireRuntimeType(string typeName)
        {
            Type runtimeType = Type.GetType($"{typeName}, {RuntimeAssemblyName}", false);
            if (runtimeType == null)
            {
                throw new TypeLoadException($"测试找不到运行时类型：{typeName}, {RuntimeAssemblyName}");
            }

            return runtimeType;
        }

        /// <summary>按运行时类型名给对象添加真实生产组件。</summary>
        public static Component AddRuntimeComponent(GameObject target, string typeName)
        {
            Type componentType = RequireRuntimeType(typeName);
            if (!typeof(Component).IsAssignableFrom(componentType))
            {
                throw new InvalidOperationException($"{typeName} 不是 Unity Component。");
            }

            return target.AddComponent(componentType);
        }

        /// <summary>按运行时类型名创建真实生产 ScriptableObject。</summary>
        public static ScriptableObject CreateRuntimeScriptableObject(string typeName)
        {
            Type scriptableObjectType = RequireRuntimeType(typeName);
            if (!typeof(ScriptableObject).IsAssignableFrom(scriptableObjectType))
            {
                throw new InvalidOperationException($"{typeName} 不是 ScriptableObject。");
            }

            return ScriptableObject.CreateInstance(scriptableObjectType);
        }

        /// <summary>设置生产对象的必需字段，兼容公开配置与私有序列化字段。</summary>
        public static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = FindRequiredField(target.GetType(), fieldName);
            field.SetValue(target, value);
        }

        /// <summary>读取生产对象公开或私有属性，并校验返回值类型。</summary>
        public static T GetProperty<T>(object target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName, AllInstanceMembers);
            if (property == null)
            {
                throw new MissingMemberException(target.GetType().Name, propertyName);
            }

            return (T)property.GetValue(target, null);
        }

        /// <summary>读取生产对象公开或私有字段，用于验证池化生命周期后的来源清理。</summary>
        public static T GetFieldValue<T>(object target, string fieldName)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            FieldInfo field = FindRequiredField(target.GetType(), fieldName);
            return (T)field.GetValue(target);
        }

        /// <summary>调用参数数量和运行时类型匹配的生产方法，并展开反射包装异常。</summary>
        public static object Invoke(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = FindRequiredMethod(
                target.GetType(),
                methodName,
                arguments,
                AllInstanceMembers);

            try
            {
                return method.Invoke(target, arguments);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        /// <summary>调用默认运行时程序集中的静态生产方法，并展开反射包装异常。</summary>
        public static object InvokeStatic(Type targetType, string methodName, params object[] arguments)
        {
            if (targetType == null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }

            MethodInfo method = FindRequiredMethod(
                targetType,
                methodName,
                arguments,
                AllStaticMembers);

            try
            {
                return method.Invoke(null, arguments);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        /// <summary>沿继承链查找字段，确保私有字段也能得到清晰的缺失错误。</summary>
        private static FieldInfo FindRequiredField(Type targetType, string fieldName)
        {
            Type currentType = targetType;
            while (currentType != null)
            {
                FieldInfo field = currentType.GetField(
                    fieldName,
                    AllInstanceMembers | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field;
                }

                currentType = currentType.BaseType;
            }

            throw new MissingFieldException(targetType.Name, fieldName);
        }

        /// <summary>按参数兼容性选择方法，避免重载入口被错误调用。</summary>
        private static MethodInfo FindRequiredMethod(
            Type targetType,
            string methodName,
            object[] arguments,
            BindingFlags memberFlags)
        {
            MethodInfo[] methods = targetType.GetMethods(memberFlags);
            for (int methodIndex = 0; methodIndex < methods.Length; methodIndex++)
            {
                MethodInfo candidate = methods[methodIndex];
                if (candidate.Name != methodName)
                {
                    continue;
                }

                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length != arguments.Length)
                {
                    continue;
                }

                bool allParametersMatch = true;
                for (int parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
                {
                    object argument = arguments[parameterIndex];
                    if (argument != null &&
                        !parameters[parameterIndex].ParameterType.IsInstanceOfType(argument))
                    {
                        allParametersMatch = false;
                        break;
                    }
                }

                if (allParametersMatch)
                {
                    return candidate;
                }
            }

            throw new MissingMethodException(
                targetType.Name,
                $"{methodName}({arguments.Length} 个参数)");
        }
    }
}
