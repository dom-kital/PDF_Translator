using ReactiveUI;

namespace PDFTranslator.GUI.ViewModels;

/// <summary>
/// 所有视图模型的基类。
/// 继承自 ReactiveObject，这是 ReactiveUI 框架的基础类，
/// 提供了 INotifyPropertyChanged 的实现以及 RaiseAndSetIfChanged 等辅助方法，
/// 使得视图模型能够轻松地实现属性变更通知，从而自动更新绑定的 UI。
/// </summary>
public abstract class ViewModelBase : ReactiveObject
{
    /// <summary>
    /// 构造函数，可以在这里添加全局初始化逻辑
    /// </summary>
    protected ViewModelBase()
    {
        // 可以在这里添加所有视图模型共用的初始化代码
    }

    /// <summary>
    /// 析构函数，用于清理资源
    /// </summary>
    ~ViewModelBase()
    {
        // 可以在这里添加所有视图模型共用的清理代码
    }

    /// <summary>
    /// 获取当前视图模型的调试字符串
    /// </summary>
    public override string ToString()
    {
        return GetType().Name;
    }
}