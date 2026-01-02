# TrainMeX Edge Case Analysis

## Overview

This document provides a comprehensive analysis of edge cases across all TrainMeX components, identifying potential failure scenarios, boundary conditions, error handling gaps, and robustness issues.

**Analysis Date:** 2024  
**Scope:** All application components  
**Purpose:** Identify edge cases for testing, documentation, and potential code improvements

---

## 1. File System & Path Handling

### Component: `FileValidator.cs`

#### Current Coverage
- ✅ Very long paths (200+ characters)
- ✅ Unicode characters in paths
- ✅ Paths with only dots (`...`)
- ✅ Paths with only slashes (`\\`)

#### Additional Edge Cases Identified

| Edge Case | Current Handling | Risk Level | Recommendation |
|-----------|-----------------|------------|----------------|
| **Network paths (UNC)** | `Path.GetFullPath()` may handle, but network disconnection not handled | Medium | Add network path detection and handle disconnection gracefully |
| **Reserved Windows names** | `Path.GetFullPath()` may throw or normalize incorrectly | Medium | Explicit check for CON, PRN, AUX, NUL, COM1-9, LPT1-9 |
| **Trailing spaces/dots** | Windows allows but may cause issues | Low | Trim trailing spaces/dots before validation |
| **Relative paths** | `Path.IsPathRooted()` check exists, but relative paths may pass validation | Medium | Ensure absolute paths only, reject relative paths explicitly |
| **Paths pointing to directories** | `File.Exists()` returns true for directories | High | Check if path is a directory using `File.GetAttributes()` |
| **Invalid characters** | `Path.GetFullPath()` throws on invalid chars | Low | Already handled by try-catch, but could provide better error messages |
| **MAX_PATH exceeded** | May fail silently on older Windows | Medium | Support `\\?\` prefix for long paths |
| **File locked by another process** | `File.Exists()` may succeed, but file inaccessible | Medium | Attempt file open to verify accessibility |
| **File deleted between validation and playback** | No re-validation before playback | High | Re-validate file existence before setting MediaElement source |
| **File permissions (read-only)** | File exists but may not be readable | Medium | Check file permissions or attempt read access |
| **Symlinks/junction points** | May resolve incorrectly | Low | Use `File.GetAttributes()` to detect and handle |
| **Removable drive disconnection** | Path valid but drive unavailable | Medium | Check drive availability before validation |
| **Case sensitivity** | Windows is case-insensitive, but may cause confusion | Low | Normalize case for consistency |

#### Code Analysis

**`IsValidPath()` Method:**
- ✅ Handles null/whitespace
- ✅ Uses try-catch for exceptions
- ✅ Checks for `..` after normalization
- ⚠️ Does not verify file vs directory
- ⚠️ Does not handle network paths specially
- ⚠️ Does not check file accessibility

**`ValidateVideoFile()` Method:**
- ✅ Validates path
- ✅ Validates extension
- ✅ Checks file existence
- ⚠️ No re-validation before playback
- ⚠️ No check for directory vs file

**`SanitizePath()` Method:**
- ✅ Normalizes path
- ✅ Checks for `..`
- ⚠️ Does not handle long paths with `\\?\` prefix
- ⚠️ Does not verify file vs directory

---

## 2. Video Playback

### Components: `HypnoViewModel.cs`, `VideoPlayerService.cs`

#### Current Coverage
- ✅ Media errors handled with `OnMediaFailed()`
- ✅ Empty queue check in `PlayNext()`

#### Additional Edge Cases Identified

| Edge Case | Current Handling | Risk Level | Recommendation |
|-----------|-----------------|------------|----------------|
| **Empty video queue** | `PlayNext()` returns early if `_files.Length == 0` | Low | ✅ Handled correctly |
| **Single video in queue** | Loops back to index 0 | Low | ✅ Expected behavior |
| **All videos fail to play** | `OnMediaFailed()` has failure counter and stops after N failures | ✅ Fixed | ✅ Failure counter added, stops after 10 consecutive failures |
| **Corrupted video files** | MediaElement may fail to load | Medium | Already handled by `OnMediaFailed()`, but could validate file headers |
| **Zero-length video files** | May cause MediaElement issues | Medium | Check file size before adding to queue |
| **No audio track** | Video plays silently | Low | ✅ Acceptable behavior |
| **No video track (audio-only)** | May cause display issues | Low | ✅ Acceptable behavior |
| **Very large video files** | Memory/performance issues | Medium | Already warns, but could limit queue size |
| **Unusual codecs** | MediaElement may not support | Medium | Already handled by `OnMediaFailed()` |
| **Network streaming URLs** | Not currently supported | Low | Document limitation |
| **Rapid queue changes** | Race condition possible | Medium | Add queue locking or cancellation token |
| **Rapid PlayNext() calls** | May skip videos or cause issues | Medium | Add debouncing or state check |
| **MediaElement disposed while playing** | NullReferenceException possible | High | Check for disposal before operations |
| **Screen disconnected during playback** | Window may be on invalid screen | High | Monitor screen changes, handle gracefully |
| **Multiple windows on same screen** | Overlapping windows | Low | Document behavior or prevent |

#### Code Analysis

**`HypnoViewModel.PlayNext()`:**
- ✅ Checks for empty/null files array
- ✅ Handles looping correctly
- ⚠️ No protection against rapid calls
- ⚠️ No failure tracking (infinite retry possible)

**`HypnoViewModel.LoadCurrentVideo()`:**
- ✅ Checks bounds
- ✅ Handles null current item
- ⚠️ No re-validation of file existence
- ⚠️ No check if MediaElement is disposed

**`HypnoViewModel.OnMediaFailed()`:**
- ✅ Logs error
- ✅ Raises event
- ✅ Calls `PlayNext()` to skip
- ✅ **FIXED**: Failure counter prevents infinite loop (stops after 10 consecutive failures)
- ✅ Per-file failure tracking (skips files after 3 failures)

**`VideoPlayerService.PlayOnScreens()`:**
- ✅ Stops all before starting
- ✅ Normalizes items
- ⚠️ No validation that screens are still valid
- ⚠️ No check for empty queue

**`VideoPlayerService.NormalizeItemsAsync()`:**
- ✅ Filters out non-rooted paths
- ✅ Checks file existence asynchronously
- ✅ **FIXED**: Fully async method using `ConfigureAwait(false)` - no deadlock risk
- ⚠️ No timeout on file existence check (but has retry logic with exponential backoff)

---

## 3. Screen/Monitor Management

### Components: `ScreenViewer.cs`, `LauncherViewModel.cs`

#### Current Coverage
- ✅ No screens detected (fallback to primary)

#### Additional Edge Cases Identified

| Edge Case | Current Handling | Risk Level | Recommendation |
|-----------|-----------------|------------|----------------|
| **Screen disconnected during runtime** | `SystemEvents.DisplaySettingsChanged` handler exists | Medium | ✅ Handled, but verify window cleanup |
| **Screen resolution changed** | `SystemEvents.DisplaySettingsChanged` handler exists | Medium | ✅ Handled, but verify window repositioning |
| **DPI scaling changes** | DPI considered in `HypnoWindow` | Low | ✅ Handled |
| **Screen with zero dimensions** | `Screen.Bounds` unlikely to be zero | Low | Add validation check |
| **Screen with negative coordinates** | Possible with multi-monitor setups | Low | Handle gracefully |
| **Primary screen changes** | `Screen.Primary` property used | Medium | Verify fallback logic |
| **Screen order changes** | Device names may change | Medium | Use stable identifiers if possible |
| **ScreenViewer with null Screen** | Constructor requires Screen parameter | Low | ✅ Protected by constructor |
| **ScreenViewer with null DeviceName** | `Screen.DeviceName` unlikely null | Low | Add null check in `ToString()` |
| **Rapid screen refresh calls** | Cache timeout (5 seconds) prevents excessive calls | Low | ✅ Handled by caching |
| **Screen cache timeout edge cases** | 5-second cache may be too short/long | Low | Consider configurable timeout |

#### Code Analysis

**`LauncherViewModel.RefreshScreens()`:**
- ✅ Uses caching to prevent excessive calls
- ✅ Handles exceptions
- ✅ Provides fallback to primary screen
- ⚠️ Cache timeout may not be optimal
- ⚠️ No validation of screen dimensions

**`LauncherViewModel.SystemEvents_DisplaySettingsChanged()`:**
- ✅ Invalidates cache
- ✅ Refreshes screens on UI thread
- ⚠️ Does not handle windows on disconnected screens

**`ScreenViewer.ToString()`:**
- ✅ Handles null DeviceName with `string.IsNullOrEmpty()`
- ✅ Extracts screen number from DeviceName
- ⚠️ Could fail if DeviceName format unexpected

---

## 4. LRU Cache

### Component: `LruCache.cs`

#### Current Coverage
- ✅ Zero max size (throws NullReferenceException - documented)
- ✅ Negative max size (handled in test)

#### Additional Edge Cases Identified

| Edge Case | Current Handling | Risk Level | Recommendation |
|-----------|-----------------|------------|----------------|
| **Max size of 1** | Should work but immediate eviction | Low | ✅ Acceptable behavior |
| **Very large max size** | Memory concerns | Medium | Consider upper limit |
| **TTL expiration during Get** | Checked before returning value | Low | ✅ Handled correctly |
| **TTL expiration during Set** | Not checked, entry created with new timestamp | Low | ✅ Acceptable (entry refreshed) |
| **Concurrent access** | No thread safety | High | Add locking or use concurrent collections |
| **Null keys** | Dictionary allows null keys | Medium | Explicitly allow or reject null keys |
| **Null values** | Allowed by design | Low | ✅ Acceptable |
| **Key equality edge cases** | Uses default comparer | Low | ✅ Acceptable for string keys |
| **Cache at exactly max size** | Next Set will evict LRU | Low | ✅ Expected behavior |
| **Clear() during active operations** | Clears both collections | Low | ✅ Safe operation |
| **Remove() non-existent key** | Checks existence before removal | Low | ✅ Handled correctly |

#### Code Analysis

**`LruCache.TryGetValue()`:**
- ✅ Checks expiration
- ✅ Moves to front (LRU update)
- ⚠️ Not thread-safe

**`LruCache.Set()`:**
- ✅ Updates existing entries
- ✅ Evicts LRU when at capacity
- ⚠️ Not thread-safe
- ⚠️ No validation of max size

**`LruCache.Remove()`:**
- ✅ Checks existence before removal
- ⚠️ Not thread-safe

**Thread Safety Issue:**
- ⚠️ **CRITICAL**: All operations are not thread-safe
- Used in `VideoPlayerService` which may have concurrent access
- Recommendation: Add `lock` statements or use `ConcurrentDictionary`

---

## 5. Service Container

### Component: `ServiceContainer.cs`

#### Current Coverage
- ✅ Multiple registrations (overwrites)

#### Additional Edge Cases Identified

| Edge Case | Current Handling | Risk Level | Recommendation |
|-----------|-----------------|------------|----------------|
| **Register null service** | Throws `ArgumentNullException` | Low | ✅ Correct behavior |
| **Get unregistered service** | Throws `InvalidOperationException` | Low | ✅ Correct behavior with helpful message |
| **TryGet unregistered service** | Returns false | Low | ✅ Correct behavior |
| **Thread safety** | No locking, static dictionary | High | Add thread safety for concurrent access |
| **Service disposal lifecycle** | No disposal tracking | Medium | Consider IDisposable support |
| **Circular dependencies** | Not applicable (no factory pattern) | Low | N/A |
| **Generic type edge cases** | Uses `typeof(T)` correctly | Low | ✅ Handled correctly |

#### Code Analysis

**`ServiceContainer.Register<T>()`:**
- ✅ Null check with exception
- ✅ Overwrites existing registration
- ⚠️ Not thread-safe

**`ServiceContainer.Get<T>()`:**
- ✅ Throws helpful exception if not registered
- ⚠️ Not thread-safe

**`ServiceContainer.TryGet<T>()`:**
- ✅ Returns false if not found
- ✅ Type checking with `is T`
- ⚠️ Not thread-safe

**Thread Safety Issue:**
- ⚠️ **CRITICAL**: Static dictionary not thread-safe
- Used during application startup (single-threaded) but could be accessed concurrently later
- Recommendation: Use `ConcurrentDictionary` or add locking

---

## 6. Settings Management

### Component: `UserSettings.cs`

#### Current Coverage
- ✅ Basic load/save with defaults

#### Additional Edge Cases Identified

| Edge Case | Current Handling | Risk Level | Recommendation |
|-----------|-----------------|------------|----------------|
| **Corrupted settings.json** | Try-catch returns defaults | Low | ✅ Handled correctly |
| **Settings file locked** | Try-catch returns defaults on load | Medium | Could retry or show warning |
| **Settings file missing** | Returns new instance with defaults | Low | ✅ Correct behavior |
| **Invalid values (negative opacity, >1.0 volume)** | No validation | Medium | Add validation in setters or load |
| **Out-of-range hotkey modifiers** | No validation | Medium | Validate modifier values |
| **Invalid hotkey key names** | `GlobalHotkeyService` handles with fallback | Low | ✅ Handled downstream |
| **Disk full during save** | Exception caught and logged | Medium | ✅ Handled, but user not notified |
| **Permissions denied during save** | Exception caught and logged | Medium | ✅ Handled, but user not notified |
| **Settings file in read-only location** | Exception caught and logged | Medium | ✅ Handled, but user not notified |
| **Concurrent save operations** | No locking | Medium | Add file locking or queue saves |
| **Settings loaded during shutdown** | May cause issues | Low | Check application state |

#### Code Analysis

**`UserSettings.Load()`:**
- ✅ Handles file not found
- ✅ Handles exceptions with defaults
- ⚠️ No validation of loaded values
- ⚠️ No handling of partial JSON (may throw)

**`UserSettings.Save()`:**
- ✅ Handles exceptions
- ⚠️ No file locking
- ⚠️ No user notification on failure
- ⚠️ No validation before save

**Property Setters:**
- ⚠️ No validation (opacity/volume can be out of range)
- ⚠️ No bounds checking

---

## 7. Playlist Management

### Components: `Playlist.cs`, `LauncherViewModel.cs`

#### Current Coverage
- ✅ Null items (test exists but doesn't verify behavior)

#### Additional Edge Cases Identified

| Edge Case | Current Handling | Risk Level | Recommendation |
|-----------|-----------------|------------|----------------|
| **Empty playlist file** | Deserializes to empty list | Low | ✅ Handled correctly |
| **Duplicate file paths** | Allowed, added to collection | Low | ✅ Acceptable behavior |
| **Invalid screen device names** | Falls back to first available screen | Medium | ✅ Handled with fallback |
| **Missing files** | Validated, marked as Missing status | Low | ✅ Handled correctly |
| **Very large number of items** | No limit | Medium | Consider performance impact |
| **Playlist file locked** | Exception caught | Medium | ✅ Handled, but could retry |
| **Corrupted playlist (invalid JSON)** | Exception caught | Medium | ✅ Handled with error message |
| **Null FilePath entries** | `VideoItem` constructor accepts null | Medium | Validate before creating VideoItem |
| **Invalid opacity/volume values** | No validation | Medium | Validate and clamp values |
| **Playlist save during active playback** | No locking | Low | ✅ Acceptable (read-only during playback) |
| **Playlist load while files being added** | May cause race condition | Medium | Consider locking or queue operations |

#### Code Analysis

**`LauncherViewModel.SavePlaylist()`:**
- ✅ Serializes playlist
- ✅ Uses SaveFileDialog
- ⚠️ No validation of items before save
- ⚠️ No error handling for serialization

**`LauncherViewModel.LoadPlaylistAsync()`:**
- ✅ Handles exceptions
- ✅ Validates files
- ✅ Shows summary of loaded files
- ⚠️ No validation of opacity/volume ranges
- ⚠️ No check for null FilePath in playlist items

**`Playlist` Class:**
- ⚠️ `Items` can be set to null (no protection)
- ⚠️ No validation of `PlaylistItem` properties

---

## 8. VideoItem

### Component: `VideoItem.cs`

#### Current Coverage
- ✅ Null file path
- ✅ Very long file paths

#### Additional Edge Cases Identified

| Edge Case | Current Handling | Risk Level | Recommendation |
|-----------|-----------------|------------|----------------|
| **Empty string path** | `Path.GetFileName()` returns empty | Medium | Validate in constructor or Validate() |
| **Whitespace-only path** | Treated as valid path | Medium | Trim and validate |
| **Assigned screen becomes invalid** | No re-validation | Medium | Validate screen on use |
| **Opacity > 1.0 or < 0.0** | No validation | Medium | Clamp values in setter |
| **Volume > 1.0 or < 0.0** | No validation | Medium | Clamp values in setter |
| **Validate() on deleted file** | Marks as Missing | Low | ✅ Correct behavior |
| **Validate() on locked file** | May throw exception | Medium | Handle file access exceptions |
| **Null AssignedScreen** | Allowed | Low | ✅ Acceptable (defaults to null) |
| **FileName with no filename** | `Path.GetFileName()` handles edge cases | Low | ✅ Handled by .NET |

#### Code Analysis

**`VideoItem` Constructor:**
- ✅ Accepts null file path
- ⚠️ No validation of path format
- ⚠️ No trimming of whitespace

**`VideoItem.Validate()`:**
- ✅ Handles null/whitespace
- ✅ Uses `FileValidator.ValidateVideoFile()`
- ✅ Sets appropriate status
- ⚠️ No handling of file access exceptions (locked files)

**Property Setters (Opacity/Volume):**
- ⚠️ No validation or clamping
- ⚠️ Can set invalid values

---

## 9. Global Hotkey Service

### Component: `GlobalHotkeyService.cs`

#### Current Coverage
- ✅ Tests exist (GlobalHotkeyServiceTests.cs)
- ✅ Basic initialization and disposal tests
- ✅ Invalid key name handling (fallback to End key)
- ✅ Multiple initialization scenarios
- ✅ Reinitialize functionality

#### Additional Edge Cases Identified

| Edge Case | Current Handling | Risk Level | Recommendation |
|-----------|-----------------|------------|----------------|
| **Hotkey already registered** | `RegisterHotKey()` return value checked and logged | ✅ Fixed | ✅ Return value is checked and warning is logged |
| **Invalid key name** | Falls back to End key | Low | ✅ Handled with fallback |
| **Invalid modifiers combination** | No validation | Medium | Validate modifier values |
| **Window handle becomes invalid** | Checked in Dispose | Low | ✅ Handled |
| **Reinitialize during active registration** | Unregisters old, registers new | Low | ✅ Handled correctly |
| **Dispose without initialization** | Checks for zero handle | Low | ✅ Handled |
| **Multiple Initialize calls** | No protection | Medium | Check if already initialized |
| **Hotkey registration failure** | Return value checked and logged | ✅ Fixed | ✅ Return value is checked and warning is logged |

#### Code Analysis

**`GlobalHotkeyService.Initialize()`:**
- ✅ Parses key name with fallback
- ✅ Registers hotkey
- ✅ **FIXED**: Checks `RegisterHotKey()` return value and logs warning
- ⚠️ No check if already initialized (may overwrite previous registration)
- ⚠️ No validation of modifiers

**`GlobalHotkeyService.Reinitialize()`:**
- ✅ Unregisters old hotkey
- ✅ Registers new hotkey
- ✅ **FIXED**: Checks return values and logs warnings

**`GlobalHotkeyService.Dispose()`:**
- ✅ Checks for valid handle
- ✅ Removes hook
- ✅ Unregisters hotkey
- ✅ **FIXED**: Checks `UnregisterHotKey()` return value and logs warning

**Status:**
- ✅ **RESOLVED**: `RegisterHotKey()` failures are now detected and logged
- If hotkey is already registered by another app, warning is logged
- Return value checking implemented in Initialize(), Reinitialize(), and Dispose()

---

## 10. Async Operations & Concurrency

### Component: `LauncherViewModel.cs`

#### Current Coverage
- ✅ Cancellation tokens used

#### Additional Edge Cases Identified

| Edge Case | Current Handling | Risk Level | Recommendation |
|-----------|-----------------|------------|----------------|
| **Cancellation during file validation** | `ThrowIfCancellationRequested()` called | Low | ✅ Handled correctly |
| **Cancellation during playlist load** | Token passed to async operations | Low | ✅ Handled correctly |
| **Multiple Browse operations** | No protection | Medium | Disable browse button during operation |
| **File addition during playback start** | No locking | Medium | Consider queueing or locking |
| **Screen refresh during file addition** | Cache prevents excessive calls | Low | ✅ Handled by caching |
| **Dispose during async operations** | Cancellation token cancelled on dispose | Low | ✅ Handled correctly |
| **Task continuation exceptions** | `.ContinueWith()` with `OnlyOnFaulted` | Low | ✅ Handled correctly |
| **UI thread marshalling** | `Dispatcher.InvokeAsync()` used | Low | ✅ Handled correctly |
| **Race conditions in UpdateButtons()** | Called from UI thread | Low | ✅ Safe (single UI thread) |
| **Race conditions in UpdateAllFilesAssigned()** | Called from UI thread | Low | ✅ Safe (single UI thread) |

#### Code Analysis

**`LauncherViewModel.AddFilesAsync()`:**
- ✅ Uses cancellation token
- ✅ Validates files in parallel
- ✅ Updates UI on UI thread
- ⚠️ Uses blocking `.GetAwaiter().GetResult()` in `NormalizeItems()` (potential deadlock)
- ⚠️ No protection against concurrent calls

**`LauncherViewModel.LoadPlaylistAsync()`:**
- ✅ Uses cancellation token
- ✅ Marshals UI updates to UI thread
- ✅ Handles exceptions
- ⚠️ No protection against concurrent loads

**`VideoPlayerService.NormalizeItems()`:**
- ⚠️ **CRITICAL**: Uses blocking async call `.GetAwaiter().GetResult()`
- Called from `PlayOnScreens()` which may be on UI thread
- Can cause deadlock if called from UI thread
- Recommendation: Make `NormalizeItems()` fully async

---

## 11. Window Management

### Component: `HypnoWindow.xaml.cs`

#### Current Coverage
- ✅ Basic disposal pattern

#### Additional Edge Cases Identified

| Edge Case | Current Handling | Risk Level | Recommendation |
|-----------|-----------------|------------|----------------|
| **Window closed during playback** | `OnClosed()` calls Dispose | Low | ✅ Handled correctly |
| **Window disposed multiple times** | `_disposed` flag prevents double disposal | Low | ✅ Handled correctly |
| **Window with null target screen** | Checked before use | Low | ✅ Handled correctly |
| **Window positioning on invalid screen** | No validation screen still exists | Medium | Validate screen before positioning |
| **Window handle creation failure** | `WindowInteropHelper` may fail | Low | Handle exception |
| **MediaElement disposal edge cases** | Stopped and closed before nulling | Low | ✅ Handled correctly |
| **Event handler subscription after disposal** | Unsubscribed in Dispose | Low | ✅ Handled correctly |
| **Window shown on disconnected screen** | No validation | Medium | Check screen validity before showing |

#### Code Analysis

**`HypnoWindow.Dispose()`:**
- ✅ Implements IDisposable pattern correctly
- ✅ Unsubscribes from events
- ✅ Disposes MediaElement
- ✅ Uses `_disposed` flag
- ⚠️ No validation that screen still exists

**`HypnoWindow.Window_SourceInitialized()`:**
- ✅ Checks for null screen
- ✅ Sets window properties
- ✅ Handles DPI scaling
- ⚠️ No validation screen is still connected
- ⚠️ No error handling for window positioning failures

---

## 12. ObservableObject & Property Changes

### Component: `ObservableObject.cs`

#### Current Coverage
- ✅ Same reference check (doesn't raise event)

#### Additional Edge Cases Identified

| Edge Case | Current Handling | Risk Level | Recommendation |
|-----------|-----------------|------------|----------------|
| **SetProperty with null vs default(T)** | Uses `EqualityComparer<T>.Default` | Low | ✅ Handled correctly |
| **SetProperty during PropertyChanged handler** | No protection | Low | ✅ Acceptable (re-entrancy) |
| **PropertyChanged handler throws exception** | Exception propagates | Medium | Consider try-catch in `OnPropertyChanged()` |
| **Multiple PropertyChanged subscriptions** | .NET events handle this | Low | ✅ Handled by .NET |
| **PropertyChanged unsubscription edge cases** | .NET events handle this | Low | ✅ Handled by .NET |
| **Value type equality (structs)** | Uses default comparer | Low | ✅ Handled correctly |

#### Code Analysis

**`ObservableObject.SetProperty()`:**
- ✅ Uses `EqualityComparer<T>.Default` for comparison
- ✅ Only raises event if value changed
- ⚠️ No exception handling in `OnPropertyChanged()`

**`ObservableObject.OnPropertyChanged()`:**
- ✅ Uses null-conditional operator
- ⚠️ No exception handling (exceptions in handlers propagate)

---

## 13. RelayCommand

### Component: `RelayCommand.cs`

#### Current Coverage
- ✅ Null CanExecute predicate

#### Additional Edge Cases Identified

| Edge Case | Current Handling | Risk Level | Recommendation |
|-----------|-----------------|------------|----------------|
| **Execute with null parameter** | Accepted by Action<object> | Low | ✅ Acceptable |
| **Execute when CanExecute returns false** | No protection | Medium | Check `CanExecute()` before `Execute()` or add guard |
| **CanExecute throws exception** | Exception propagates | Medium | Consider try-catch |
| **Execute throws exception** | Exception propagates | Medium | ✅ Acceptable (caller should handle) |
| **Command execution during disposal** | No disposal pattern | Low | N/A (no resources to dispose) |
| **CanExecuteChanged event** | Uses `CommandManager.RequerySuggested` | Low | ✅ Standard WPF pattern |

#### Code Analysis

**`RelayCommand.CanExecute()`:**
- ✅ Handles null predicate
- ⚠️ No exception handling

**`RelayCommand.Execute()`:**
- ✅ No null check (delegate cannot be null per constructor)
- ⚠️ No check if `CanExecute()` returns false
- ⚠️ No exception handling

**Constructor:**
- ✅ Throws `ArgumentNullException` for null execute
- ✅ Allows null `canExecute` predicate

---

## 14. Application Lifecycle

### Component: `App.xaml.cs`

#### Current Coverage
- ✅ Unhandled exception handlers

#### Additional Edge Cases Identified

| Edge Case | Current Handling | Risk Level | Recommendation |
|-----------|-----------------|------------|----------------|
| **Application shutdown during video playback** | No cleanup in OnExit | Medium | Add cleanup in `OnExit()` override |
| **Service registration failures** | No error handling | Medium | Handle registration exceptions |
| **Settings load failure during startup** | Returns defaults | Low | ✅ Handled correctly |
| **Multiple application instances** | No single-instance enforcement | Low | Consider single-instance pattern |
| **Application restart with active session** | Session auto-loads if enabled | Low | ✅ Expected behavior |
| **Exception in global exception handler** | Could cause infinite loop | High | Add try-catch in exception handlers |

#### Code Analysis

**`App.OnStartup()`:**
- ✅ Registers global exception handlers
- ✅ Registers services
- ⚠️ No error handling for service registration
- ⚠️ No cleanup on shutdown

**Exception Handlers:**
- ✅ Log exceptions
- ✅ Show user-friendly messages
- ✅ **FIXED**: Exception handlers wrapped in try-catch to prevent infinite loops
- ⚠️ No cleanup of resources on fatal exception (may be acceptable for fatal errors)

---

## 15. Integration Edge Cases

### Cross-Component Scenarios

#### Additional Edge Cases Identified

| Edge Case | Current Handling | Risk Level | Recommendation |
|-----------|-----------------|------------|----------------|
| **File added, then immediately deleted** | Validation may pass, playback fails | Medium | Re-validate before playback |
| **Screen assigned to video, then screen disconnected** | Video may not play | Medium | Validate screen before playback |
| **Playback started with invalid files** | Files filtered in `NormalizeItems()` | Low | ✅ Handled, but could show warning |
| **Settings changed during active playback** | Changes apply to new videos | Low | ✅ Expected behavior |
| **Playlist loaded with files already in collection** | Duplicates added | Low | ✅ Acceptable, or could merge |
| **Rapid play/pause/stop operations** | No debouncing | Low | ✅ Acceptable behavior |
| **Multiple HypnoWindows on same screen** | Allowed, overlapping | Low | ✅ Acceptable or could prevent |
| **Video service operations during window disposal** | `StopAll()` creates copy of list | Low | ✅ Handled correctly |

---

## Summary of Critical Issues

### High Priority (Fix Recommended)

1. **`GlobalHotkeyService`: Hotkey registration failures ignored** ✅ **FIXED**
   - ~~`RegisterHotKey()` return value not checked~~
   - ✅ Return value now checked and logged
   - ✅ Panic hotkey failures are detected and logged

2. **`VideoPlayerService.NormalizeItems()`: Potential deadlock** ✅ **FIXED**
   - ~~Uses blocking `.GetAwaiter().GetResult()`~~
   - ✅ Method is now fully async (`NormalizeItemsAsync`)
   - ✅ Uses `ConfigureAwait(false)` to prevent deadlocks

3. **`HypnoViewModel.OnMediaFailed()`: Infinite retry loop** ✅ **FIXED**
   - ~~If all videos fail, continuously retries~~
   - ✅ Failure counter added (`MaxConsecutiveFailures = 10`)
   - ✅ Per-file failure tracking (skips after 3 failures per file)

4. **`App.xaml.cs`: Exception handlers can throw** ✅ **FIXED**
   - ~~Exception in handler could cause issues~~
   - ✅ Exception handlers wrapped in try-catch blocks
   - ✅ Prevents infinite exception loops

### Medium Priority (Consider Fixing)

1. **Thread safety: `LruCache`, `ServiceContainer`**
   - Not thread-safe but may be accessed concurrently
   - **Fix**: Add locking or use concurrent collections

2. **File validation: Directory vs file**
   - `FileValidator` doesn't check if path is directory
   - **Fix**: Add directory check

3. **Settings validation: Invalid values**
   - No validation of opacity/volume ranges
   - **Fix**: Add validation/clamping

4. **Screen validation: Disconnected screens**
   - Windows may be positioned on invalid screens
   - **Fix**: Validate screens before use

### Low Priority (Nice to Have)

1. **Path handling: Long paths, network paths**
2. **VideoItem: Property validation**
3. **Playlist: Null FilePath validation**
4. **ObservableObject: Exception handling in handlers**

---

## Test Coverage Gaps

### Missing Edge Case Tests

1. **FileValidator**
   - Network paths (UNC)
   - Reserved Windows names
   - Directory vs file check
   - File locked scenarios
   - Long paths with `\\?\` prefix

2. **VideoPlayerService**
   - All videos fail scenario
   - Rapid PlayNext() calls
   - MediaElement disposal
   - Screen disconnected during playback

3. **LruCache**
   - Thread safety (concurrent access)
   - Max size of 1
   - Null keys

4. **ServiceContainer**
   - Thread safety
   - TryGet with wrong type

5. **UserSettings**
   - Invalid values (out of range)
   - File locked scenarios
   - Corrupted JSON

6. **GlobalHotkeyService**
   - Hotkey registration failure
   - Invalid key names
   - Multiple Initialize calls

7. **HypnoViewModel**
   - Empty queue handling
   - All videos fail scenario
   - Rapid queue changes

8. **VideoItem**
   - Invalid opacity/volume values
   - Empty/whitespace paths
   - File locked during validation

9. **LauncherViewModel**
   - Concurrent Browse operations
   - Playlist load during file addition
   - Screen disconnected scenarios

10. **HypnoWindow**
    - Window on disconnected screen
    - Multiple disposal calls
    - MediaElement disposal edge cases

---

## Recommendations

### Immediate Actions

1. **Fix critical issues** (High Priority section)
2. **Add thread safety** to `LruCache` and `ServiceContainer`
3. **Add validation** to settings and VideoItem properties
4. **Improve error handling** in hotkey service

### Testing Improvements

1. **Add edge case tests** for all identified gaps
2. **Add integration tests** for cross-component scenarios
3. **Add stress tests** for rapid operations
4. **Add concurrency tests** for thread safety

### Code Quality

1. **Add XML documentation** for edge case behaviors
2. **Add validation** at component boundaries
3. **Improve error messages** for better debugging
4. **Add logging** for edge case scenarios

---

## Conclusion

This analysis identified **15 major component areas** with **100+ edge cases**, including:

- **4 Critical Issues** - ✅ **ALL FIXED**
  - ✅ GlobalHotkeyService hotkey registration failures (now checked and logged)
  - ✅ VideoPlayerService.NormalizeItems() deadlock (now fully async)
  - ✅ HypnoViewModel.OnMediaFailed() infinite retry loop (failure counter added)
  - ✅ App.xaml.cs exception handlers (wrapped in try-catch)
- **4 Medium Priority Issues** to consider fixing
- **10+ Test Coverage Gaps** to address

The application demonstrates **excellent error handling** in most areas. All critical issues identified have been resolved. The codebase now includes:
- Proper async/await patterns to prevent deadlocks
- Failure tracking and limits to prevent infinite loops
- Comprehensive exception handling with nested try-catch blocks
- Return value checking for critical operations

Most edge cases are handled gracefully through try-catch blocks and fallback logic. Remaining improvements (thread safety, additional validation) would further enhance robustness but are not critical for production use.

