# TrainMeX Tests

This directory contains comprehensive unit and integration tests for the TrainMeX application.

## Test Framework

- **xUnit**: Primary testing framework
- **Moq**: Mocking framework (for future use with complex dependencies)
- **FluentAssertions**: Fluent assertion library (for more readable assertions)

## Test Coverage

### Unit Tests

1. **FileValidatorTests.cs** - Tests file path validation, extension checking, file size validation, and path sanitization
2. **LruCacheTests.cs** - Tests LRU cache eviction, TTL expiration, capacity management
3. **ServiceContainerTests.cs** - Tests dependency injection container registration and retrieval
4. **RelayCommandTests.cs** - Tests MVVM command pattern implementation
5. **ObservableObjectTests.cs** - Tests property change notification system
6. **VideoItemTests.cs** - Tests video item properties, validation, and state management
7. **UserSettingsTests.cs** - Tests settings loading, saving, and defaults
8. **PlaylistTests.cs** - Tests playlist serialization and deserialization
9. **VideoPlayerServiceTests.cs** - Tests video player service functionality
10. **HypnoViewModelTests.cs** - Tests video playback view model
11. **LauncherViewModelTests.cs** - Tests main launcher view model (limited due to WPF dependencies)
12. **ScreenViewerTests.cs** - Tests screen viewer wrapper
13. **ConstantsTests.cs** - Tests application constants

### Integration Tests

- **IntegrationTests.cs** - Tests integration between multiple components

### Edge Case Tests

- **EdgeCaseTests.cs** - Tests edge cases, boundary conditions, and error handling

## Running Tests

### From Command Line

```bash
dotnet test
```

### From Visual Studio

1. Open Test Explorer (Test > Test Explorer)
2. Run All Tests

### With Coverage

```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

## Test Structure

Each test class follows the Arrange-Act-Assert pattern:

```csharp
[Fact]
public void TestName_Scenario_ExpectedBehavior() {
    // Arrange
    var testData = SetupTestData();
    
    // Act
    var result = SystemUnderTest.Method(testData);
    
    // Assert
    Assert.Expected(result);
}
```

## Notes

- Some tests may require WPF Application context (LauncherViewModel tests)
- File system tests use temporary directories that are cleaned up automatically
- Tests are designed to be independent and can run in any order
- Integration tests verify that components work together correctly

## Future Improvements

- Add more mocking for WPF-dependent components
- Add performance/load tests
- Add UI automation tests
- Increase coverage for LauncherViewModel
- Add tests for WindowServices and GlobalHotkeyService

