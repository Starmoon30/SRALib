using System;
using RimWorld.QuestGen;
using Verse;
using System.Collections.Generic;
using System.Linq;

namespace SRA
{
    public class QuestNode_WriteToEventVariablesWithAdd : QuestNode
    {
        // 要写入的变量名（在EventVariableManager中的名字）
        [NoTranslate]
        public SlateRef<string> targetVariableName;
        
        // 要从Quest中读取的变量名（Slate中的变量）
        [NoTranslate]
        public SlateRef<string> sourceVariableName;
        
        // 如果sourceVariableName为空，使用这个值
        public SlateRef<object> value;
        
        // 写入模式
        public WriteMode writeMode = WriteMode.Set;
        
        // 是否检查变量已存在
        public SlateRef<bool> checkExisting = true;
        
        // 如果变量已存在，是否覆盖（仅用于Set模式）
        public SlateRef<bool> overwrite = false;
        
        // 操作符（用于Add和Multiply模式）
        public MathOperator mathOperator = MathOperator.Add;
        
        // 是否强制转换类型（当类型不匹配时）
        public SlateRef<bool> forceTypeConversion = false;
        
        // 当类型不匹配且无法转换时的行为
        public TypeMismatchBehavior onTypeMismatch = TypeMismatchBehavior.ConvertToString;
        
        // 写入后是否从Slate中删除源变量
        public SlateRef<bool> removeFromSlate = false;
        
        // 是否记录调试信息
        public SlateRef<bool> logDebug = false;
        
        // 允许操作的数值类型
        public List<Type> allowedNumericTypes = new List<Type>
        {
            typeof(int),
            typeof(float),
            typeof(double),
            typeof(long),
            typeof(short),
            typeof(decimal)
        };

        public enum WriteMode
        {
            Set,        // 直接设置值（覆盖）
            Add,        // 相加（仅对数值类型）
            Multiply,   // 相乘（仅对数值类型）
            Append,     // 追加（字符串、列表等）
            Min,        // 取最小值（仅对数值类型）
            Max,        // 取最大值（仅对数值类型）
            Increment   // 自增1（仅对数值类型，忽略源值）
        }

        public enum MathOperator
        {
            Add,        // 加法
            Subtract,   // 减法
            Multiply,   // 乘法
            Divide      // 除法
        }

        public enum TypeMismatchBehavior
        {
            ThrowError,     // 抛出错误
            Skip,           // 跳过操作
            ConvertToString,// 转换为字符串
            UseDefault      // 使用默认值
        }

        protected override bool TestRunInt(Slate slate)
        {
            return RunInternal(slate, isTestRun: true);
        }

        protected override void RunInt()
        {
            RunInternal(QuestGen.slate, isTestRun: false);
        }

        private bool RunInternal(Slate slate, bool isTestRun)
        {
            // 获取目标变量名
            string targetName = targetVariableName.GetValue(slate);
            if (string.IsNullOrEmpty(targetName))
            {
                Log.Message("[QuestNode_WriteToEventVariablesWithAdd] targetVariableName is null or empty!");
                return false;
            }

            // 获取要操作的值
            object sourceValue = GetSourceValue(slate);
            if (sourceValue == null && writeMode != WriteMode.Increment)
            {
                if (logDebug.GetValue(slate))
                {
                    Log.Message($"[QuestNode_WriteToEventVariablesWithAdd] No value to operate for variable '{targetName}'.");
                }
                return false;
            }

            // 在测试运行时不需要实际写入
            if (isTestRun)
            {
                return true;
            }

            // 获取EventVariableManager
            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            if (eventVarManager == null)
            {
                Log.Message("[QuestNode_WriteToEventVariablesWithAdd] EventVariableManager not found!");
                return false;
            }

            bool variableExists = eventVarManager.HasVariable(targetName);
            object currentValue = variableExists ? eventVarManager.GetVariable<object>(targetName) : null;

            // 记录操作前的状态
            if (logDebug.GetValue(slate))
            {
                Log.Message($"[QuestNode_WriteToEventVariablesWithAdd] Operation for variable '{targetName}':");
                Log.Message($"  - Mode: {writeMode}");
                Log.Message($"  - Current value: {currentValue ?? "[null]"} (Type: {currentValue?.GetType().Name ?? "null"})");
                Log.Message($"  - Source value: {sourceValue ?? "[null]"} (Type: {sourceValue?.GetType().Name ?? "null"})");
            }

            // 根据写入模式计算新值
            object newValue = CalculateNewValue(currentValue, sourceValue, slate);

            // 如果新值为null（可能是由于类型不匹配被跳过了）
            if (newValue == null)
            {
                if (logDebug.GetValue(slate))
                {
                    Log.Message($"  - Operation skipped (new value is null)");
                }
                return false;
            }

            // 写入变量
            eventVarManager.SetVariable(targetName, newValue);

            // 写入后从Slate中删除源变量
            if (removeFromSlate.GetValue(slate) && !string.IsNullOrEmpty(sourceVariableName.GetValue(slate)))
            {
                slate.Remove(sourceVariableName.GetValue(slate));
                if (logDebug.GetValue(slate))
                {
                    Log.Message($"  - Removed source variable '{sourceVariableName.GetValue(slate)}' from Slate");
                }
            }

            // 验证写入
            if (logDebug.GetValue(slate))
            {
                object readBackValue = eventVarManager.GetVariable<object>(targetName);
                bool writeVerified = (readBackValue == null && newValue == null) || 
                                     (readBackValue != null && readBackValue.Equals(newValue));
                
                Log.Message($"  - New value written: {newValue} (Type: {newValue.GetType().Name})");
                Log.Message($"  - Write verified: {writeVerified}");
                if (!writeVerified)
                {
                    Log.Message($"  - Verification failed! Expected: {newValue}, Got: {readBackValue}");
                }
            }

            return true;
        }

        private object GetSourceValue(Slate slate)
        {
            string sourceName = sourceVariableName.GetValue(slate);
            
            if (!string.IsNullOrEmpty(sourceName))
            {
                if (slate.TryGet<object>(sourceName, out var slateValue))
                {
                    return slateValue;
                }
            }
            
            return value.GetValue(slate);
        }

        private object CalculateNewValue(object currentValue, object sourceValue, Slate slate)
        {
            switch (writeMode)
            {
                case WriteMode.Set:
                    return HandleSetMode(currentValue, sourceValue, slate);
                    
                case WriteMode.Add:
                case WriteMode.Multiply:
                    return HandleMathOperation(currentValue, sourceValue, writeMode == WriteMode.Add, slate);
                    
                case WriteMode.Append:
                    return HandleAppendMode(currentValue, sourceValue, slate);
                    
                case WriteMode.Min:
                    return HandleMinMaxMode(currentValue, sourceValue, isMin: true, slate);
                    
                case WriteMode.Max:
                    return HandleMinMaxMode(currentValue, sourceValue, isMin: false, slate);
                    
                case WriteMode.Increment:
                    return HandleIncrementMode(currentValue, slate);
                    
                default:
                    return sourceValue;
            }
        }

        private object HandleSetMode(object currentValue, object sourceValue, Slate slate)
        {
            bool variableExists = currentValue != null;
            
            if (checkExisting.GetValue(slate) && variableExists)
            {
                if (!overwrite.GetValue(slate))
                {
                    if (logDebug.GetValue(slate))
                    {
                        Log.Message($"  - Variable exists and overwrite is false, keeping current value");
                    }
                    return currentValue; // 不覆盖，返回原值
                }
            }
            
            return sourceValue;
        }

        private object HandleMathOperation(object currentValue, object sourceValue, bool isAddition, Slate slate)
        {
            // 如果当前值不存在，直接使用源值
            if (currentValue == null)
            {
                return sourceValue;
            }

            // 尝试进行数学运算
            try
            {
                // 检查类型是否支持数学运算
                if (!IsNumericType(currentValue.GetType()) || !IsNumericType(sourceValue.GetType()))
                {
                    return HandleTypeMismatch(currentValue, sourceValue, "non-numeric", slate);
                }

                // 转换为动态类型以进行数学运算
                dynamic current = ConvertToBestNumericType(currentValue);
                dynamic source = ConvertToBestNumericType(sourceValue);
                
                dynamic result;
                
                switch (mathOperator)
                {
                    case MathOperator.Add:
                        result = current + source;
                        break;
                    case MathOperator.Subtract:
                        result = current - source;
                        break;
                    case MathOperator.Multiply:
                        result = current * source;
                        break;
                    case MathOperator.Divide:
                        if (source == 0)
                        {
                            Log.Message($"[QuestNode_WriteToEventVariablesWithAdd] Division by zero for variable operation");
                            return currentValue;
                        }
                        result = current / source;
                        break;
                    default:
                        result = current + source;
                        break;
                }

                // 根据配置决定返回类型
                if (forceTypeConversion.GetValue(slate))
                {
                    // 转换为与当前值相同的类型
                    return Convert.ChangeType(result, currentValue.GetType());
                }
                else
                {
                    // 返回最佳类型（通常是double或decimal）
                    return result;
                }
            }
            catch (Exception ex)
            {
                if (logDebug.GetValue(slate))
                {
                    Log.Message($"[QuestNode_WriteToEventVariablesWithAdd] Math operation failed: {ex.Message}");
                }
                return currentValue;
            }
        }

        private object HandleAppendMode(object currentValue, object sourceValue, Slate slate)
        {
            // 如果当前值不存在，直接使用源值
            if (currentValue == null)
            {
                return sourceValue;
            }

            // 处理字符串追加
            if (currentValue is string currentStr && sourceValue is string sourceStr)
            {
                return currentStr + sourceStr;
            }
            
            // 处理列表追加
            if (currentValue is System.Collections.IEnumerable currentEnumerable && 
                sourceValue is System.Collections.IEnumerable sourceEnumerable)
            {
                try
                {
                    // 尝试创建新列表并添加所有元素
                    var resultList = new List<object>();
                    
                    foreach (var item in currentEnumerable)
                        resultList.Add(item);
                    
                    foreach (var item in sourceEnumerable)
                        resultList.Add(item);
                    
                    return resultList;
                }
                catch
                {
                    // 如果列表操作失败，回退到字符串追加
                    return currentValue.ToString() + sourceValue.ToString();
                }
            }
            
            // 其他类型：转换为字符串并追加
            return currentValue.ToString() + sourceValue.ToString();
        }

        private object HandleMinMaxMode(object currentValue, object sourceValue, bool isMin, Slate slate)
        {
            // 如果当前值不存在，直接使用源值
            if (currentValue == null)
            {
                return sourceValue;
            }

            // 检查类型是否支持比较
            if (!(currentValue is IComparable currentComparable) || !(sourceValue is IComparable sourceComparable))
            {
                return HandleTypeMismatch(currentValue, sourceValue, "non-comparable", slate);
            }

            try
            {
                int comparison = currentComparable.CompareTo(sourceComparable);
                
                if (isMin)
                {
                    // 取最小值
                    return comparison <= 0 ? currentValue : sourceValue;
                }
                else
                {
                    // 取最大值
                    return comparison >= 0 ? currentValue : sourceValue;
                }
            }
            catch (ArgumentException)
            {
                // 类型不匹配，无法比较
                return HandleTypeMismatch(currentValue, sourceValue, "type mismatch for comparison", slate);
            }
        }

        private object HandleIncrementMode(object currentValue, Slate slate)
        {
            // 如果当前值不存在，从1开始
            if (currentValue == null)
            {
                return 1;
            }

            // 检查是否是数值类型
            if (!IsNumericType(currentValue.GetType()))
            {
                if (logDebug.GetValue(slate))
                {
                    Log.Message($"[QuestNode_WriteToEventVariablesWithAdd] Cannot increment non-numeric type: {currentValue.GetType().Name}");
                }
                return currentValue;
            }

            try
            {
                dynamic current = ConvertToBestNumericType(currentValue);
                dynamic result = current + 1;
                
                if (forceTypeConversion.GetValue(slate))
                {
                    return Convert.ChangeType(result, currentValue.GetType());
                }
                else
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                if (logDebug.GetValue(slate))
                {
                    Log.Message($"[QuestNode_WriteToEventVariablesWithAdd] Increment operation failed: {ex.Message}");
                }
                return currentValue;
            }
        }

        private object HandleTypeMismatch(object currentValue, object sourceValue, string mismatchReason, Slate slate)
        {
            if (logDebug.GetValue(slate))
            {
                Log.Message($"[QuestNode_WriteToEventVariablesWithAdd] Type mismatch for operation: {mismatchReason}");
            }

            switch (onTypeMismatch)
            {
                case TypeMismatchBehavior.ThrowError:
                    throw new InvalidOperationException($"Type mismatch for operation: {mismatchReason}");
                    
                case TypeMismatchBehavior.Skip:
                    if (logDebug.GetValue(slate))
                    {
                        Log.Message($"  - Skipping operation due to type mismatch");
                    }
                    return currentValue; // 保持原值
                    
                case TypeMismatchBehavior.ConvertToString:
                    // 都转换为字符串
                    return currentValue.ToString() + " | " + sourceValue.ToString();
                    
                case TypeMismatchBehavior.UseDefault:
                    // 使用源值作为默认
                    return sourceValue;
                    
                default:
                    return currentValue;
            }
        }

        private bool IsNumericType(Type type)
        {
            if (type == null) return false;
            
            TypeCode typeCode = Type.GetTypeCode(type);
            switch (typeCode)
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    return true;
                default:
                    return false;
            }
        }

        private dynamic ConvertToBestNumericType(object value)
        {
            if (value == null) return 0;
            
            Type type = value.GetType();
            TypeCode typeCode = Type.GetTypeCode(type);
            
            switch (typeCode)
            {
                case TypeCode.Decimal:
                    return (decimal)value;
                case TypeCode.Double:
                    return (double)value;
                case TypeCode.Single:
                    return (float)value;
                case TypeCode.Int64:
                    return (long)value;
                case TypeCode.Int32:
                    return (int)value;
                case TypeCode.Int16:
                    return (short)value;
                case TypeCode.UInt64:
                    return (ulong)value;
                case TypeCode.UInt32:
                    return (uint)value;
                case TypeCode.UInt16:
                    return (ushort)value;
                case TypeCode.Byte:
                    return (byte)value;
                case TypeCode.SByte:
                    return (sbyte)value;
                default:
                    // 尝试转换
                    try
                    {
                        return Convert.ToDouble(value);
                    }
                    catch
                    {
                        return 0;
                    }
            }
        }
    }
}
