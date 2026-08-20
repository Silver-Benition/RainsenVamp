using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RainsenVampSur.Tests
{
    /// <summary>
    /// 为组件级 EditMode 测试统一管理临时对象和全局状态。
    /// 测试通过显式调用实际生命周期方法获得确定性，不依赖当前打开的场景或 Play Mode 切换。
    /// </summary>
    public abstract class EditModeComponentTestBase
    {
        private List<GameObject> _createdObjects;

        /// <summary>为每项测试重建对象登记表，并恢复正常时间流速。</summary>
        [SetUp]
        public void PrepareComponentTest()
        {
            Time.timeScale = 1f;
            _createdObjects = new List<GameObject>();
        }

        /// <summary>
        /// 解除流程管理器事件、销毁临时对象并恢复全局时间，确保失败用例不污染后续测试。
        /// </summary>
        [TearDown]
        public void CleanUpComponentTest()
        {
            GameFlowManager manager = GameFlowManager.Instance;
            if (manager != null)
            {
                TestObjectUtility.InvokeNonPublicMethod(manager, "OnDisable");
                TestObjectUtility.InvokeNonPublicMethod(manager, "OnDestroy");
            }

            Time.timeScale = 1f;

            if (_createdObjects == null)
            {
                return;
            }

            for (int index = _createdObjects.Count - 1; index >= 0; index--)
            {
                GameObject createdObject = _createdObjects[index];
                if (createdObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdObject);
                }
            }

            _createdObjects.Clear();
            _createdObjects = null;
        }

        /// <summary>
        /// 创建并登记临时 GameObject，使公共清理流程能够统一销毁它。
        /// </summary>
        /// <param name="name">用于失败日志定位的对象名称。</param>
        /// <returns>仅存在于当前 EditMode 测试内的 GameObject。</returns>
        protected GameObject CreateTrackedGameObject(string name)
        {
            GameObject createdObject = new GameObject(name);
            _createdObjects.Add(createdObject);
            return createdObject;
        }
    }

    /// <summary>
    /// 测试专用的字段配置与生命周期调用工具。
    /// 这些能力只存在于 Editor 测试程序集，不会向生产组件增加测试 API。
    /// </summary>
    internal static class TestObjectUtility
    {
        private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>设置目标组件的浮点序列化字段；字段不存在时立即抛出明确异常。</summary>
        public static void SetFloat(UnityEngine.Object target, string propertyName, float value)
        {
            SerializedProperty property = FindRequiredProperty(target, propertyName);
            property.floatValue = value;
            property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>设置目标组件的对象引用序列化字段；字段不存在时立即抛出明确异常。</summary>
        public static void SetObjectReference(
            UnityEngine.Object target,
            string propertyName,
            UnityEngine.Object value)
        {
            SerializedProperty property = FindRequiredProperty(target, propertyName);
            property.objectReferenceValue = value;
            property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>设置测试所需的私有浮点运行时状态；字段改名时立即失败。</summary>
        public static void SetPrivateFloat(object target, string fieldName, float value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, InstanceNonPublic);
            if (field == null)
            {
                throw new MissingFieldException(target.GetType().Name, fieldName);
            }

            field.SetValue(target, value);
        }

        /// <summary>
        /// 调用组件实际的非公开生命周期方法，用于在 EditMode 中确定性验证 Awake/OnEnable 等逻辑。
        /// </summary>
        public static void InvokeNonPublicMethod(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, InstanceNonPublic);
            if (method == null)
            {
                throw new MissingMethodException(target.GetType().Name, methodName);
            }

            try
            {
                method.Invoke(target, null);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        /// <summary>调用有返回值的非公开方法，并把结果转换为测试声明的类型。</summary>
        public static T InvokeNonPublicMethod<T>(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, InstanceNonPublic);
            if (method == null)
            {
                throw new MissingMethodException(target.GetType().Name, methodName);
            }

            try
            {
                return (T)method.Invoke(target, null);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        /// <summary>设置任意私有运行时字段，用于隔离依赖场景搜索的初始化步骤。</summary>
        public static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, InstanceNonPublic);
            if (field == null)
            {
                throw new MissingFieldException(target.GetType().Name, fieldName);
            }

            field.SetValue(target, value);
        }

        /// <summary>创建 SerializedObject 并取得指定字段，避免字段改名后测试静默失效。</summary>
        private static SerializedProperty FindRequiredProperty(UnityEngine.Object target, string propertyName)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            serializedObject.UpdateIfRequiredOrScript();
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new MissingFieldException(target.GetType().Name, propertyName);
            }

            return property;
        }
    }
}
