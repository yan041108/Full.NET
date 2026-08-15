namespace Full.NET.Abstractions.Results;

/// <summary>
/// 表示操作结果的通用封装，强制调用方区分成功与失败两条路径，避免返回 null 或异常的隐式契约。
/// </summary>
/// <remarks>
/// 该类型为不可变值语义，成功时 <see cref="Value"/> 有效且 <see cref="Error"/> 为 <see langword="null"/>；
/// 失败时 <see cref="Error"/> 有效且 <see cref="Value"/> 为默认值。调用方应先判断 <see cref="IsSuccess"/>
/// 再读取对应字段。
/// </remarks>
/// <typeparam name="T">成功时返回的值类型。</typeparam>
public sealed class Result<T>
{
    /// <summary>
    /// 初始化操作结果的内部构造函数，通过工厂方法确保不变量成立。
    /// </summary>
    /// <param name="isSuccess">是否成功。</param>
    /// <param name="value">成功时的值。</param>
    /// <param name="error">失败时的错误。</param>
    private Result(bool isSuccess, T? value, Error? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    /// <summary>
    /// 获取一个值，指示操作是否成功完成。
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// 获取成功操作返回的值；仅当 <see cref="IsSuccess"/> 为 <see langword="true"/> 时有意义。
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// 获取失败操作对应的错误；仅当 <see cref="IsSuccess"/> 为 <see langword="false"/> 时有意义。
    /// </summary>
    public Error? Error { get; }

    /// <summary>
    /// 创建表示成功的结果，并携带指定返回值。
    /// </summary>
    /// <param name="value">成功返回的值。</param>
    /// <returns>成功结果实例。</returns>
    public static Result<T> Success(T value) => new(true, value, null);

    /// <summary>
    /// 创建表示失败的结果，并关联指定错误。
    /// </summary>
    /// <param name="error">描述失败原因的错误实例。</param>
    /// <returns>失败结果实例。</returns>
    public static Result<T> Failure(Error error) => new(false, default, error);
}
