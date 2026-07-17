namespace Full.NET.Modules.Identity.Security;

internal interface IRandomTokenGenerator
{
    string Generate(int byteCount);
}
