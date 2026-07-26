using Full.NET.Modules.Files.Storage;

namespace Full.NET.UnitTests.Files;

[TestClass]
public sealed class LocalFileStorageOptionsValidatorTests
{
    [TestMethod]
    public void Validator_rejects_invalid_values_and_accepts_valid_configuration()
    {
        var validator = new LocalFileStorageOptionsValidator();
        var existingFile = Path.GetTempFileName();

        try
        {
            Assert.IsTrue(validator.Validate(
                null,
                new LocalFileStorageOptions { RootPath = " " }).Failed);
            Assert.IsTrue(validator.Validate(
                null,
                new LocalFileStorageOptions
                {
                    RootPath = "App_Data/files",
                    MaxUploadBytes = 0,
                }).Failed);
            Assert.IsTrue(validator.Validate(
                null,
                new LocalFileStorageOptions
                {
                    RootPath = "\0",
                    MaxUploadBytes = 1024,
                }).Failed);
            Assert.IsTrue(validator.Validate(
                null,
                new LocalFileStorageOptions
                {
                    RootPath = existingFile,
                    MaxUploadBytes = 1024,
                }).Failed);
            Assert.IsTrue(validator.Validate(
                null,
                new LocalFileStorageOptions
                {
                    RootPath = "App_Data/files",
                    MaxUploadBytes = 1024,
                }).Succeeded);
        }
        finally
        {
            File.Delete(existingFile);
        }
    }
}
