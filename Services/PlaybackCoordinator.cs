using System;
using System.Collections.Generic;

namespace sopfiy.Services;

public enum RepeatMode
{
    Off,
    RepeatAll,
    RepeatOne
}

public class PlaybackCoordinator
{
    private readonly Random _random = new();
    private readonly List<int> _shuffledIndices = new();
    private int _shufflePointer = -1;

    public RepeatMode CurrentRepeatMode { get; private set; } = RepeatMode.Off;
    public bool IsShuffleEnabled { get; private set; }
    public int LastVolumeBeforeMute { get; private set; } = 75;
    public bool IsMuted { get; private set; }

    public RepeatMode CycleRepeatMode()
    {
        CurrentRepeatMode = CurrentRepeatMode switch
        {
            RepeatMode.Off => RepeatMode.RepeatAll,
            RepeatMode.RepeatAll => RepeatMode.RepeatOne,
            RepeatMode.RepeatOne => RepeatMode.Off,
            _ => RepeatMode.Off
        };
        return CurrentRepeatMode;
    }

    public bool ToggleShuffle(int currentIndex, int totalCount)
    {
        IsShuffleEnabled = !IsShuffleEnabled;
        if (IsShuffleEnabled)
        {
            GenerateShufflePool(totalCount, currentIndex);
        }
        else
        {
            _shuffledIndices.Clear();
            _shufflePointer = -1;
        }
        return IsShuffleEnabled;
    }

    public void ResetForNewList(int totalCount, int activeIndex = -1)
    {
        if (IsShuffleEnabled && totalCount > 0)
        {
            GenerateShufflePool(totalCount, activeIndex);
        }
        else
        {
            _shuffledIndices.Clear();
            _shufflePointer = -1;
        }
    }

    private void GenerateShufflePool(int totalCount, int activeIndex)
    {
        _shuffledIndices.Clear();
        if (totalCount <= 0)
        {
            _shufflePointer = -1;
            return;
        }

        for (int i = 0; i < totalCount; i++)
        {
            _shuffledIndices.Add(i);
        }

        // Fisher-Yates shuffle
        for (int i = _shuffledIndices.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (_shuffledIndices[i], _shuffledIndices[j]) = (_shuffledIndices[j], _shuffledIndices[i]);
        }

        // Pin active track to the front if valid
        if (activeIndex >= 0 && activeIndex < totalCount)
        {
            int currentPos = _shuffledIndices.IndexOf(activeIndex);
            if (currentPos >= 0)
            {
                _shuffledIndices.RemoveAt(currentPos);
                _shuffledIndices.Insert(0, activeIndex);
            }
            _shufflePointer = 0;
        }
        else
        {
            _shufflePointer = 0;
        }
    }

    public int GetNextIndex(int currentIndex, int totalCount, bool isManualSkip = false)
    {
        if (totalCount <= 0) return -1;

        if (CurrentRepeatMode == RepeatMode.RepeatOne && !isManualSkip)
        {
            return currentIndex >= 0 ? currentIndex : 0;
        }

        if (IsShuffleEnabled)
        {
            if (_shuffledIndices.Count != totalCount)
            {
                GenerateShufflePool(totalCount, currentIndex);
            }

            if (_shufflePointer < 0 || _shufflePointer >= _shuffledIndices.Count)
            {
                _shufflePointer = _shuffledIndices.IndexOf(currentIndex);
                if (_shufflePointer < 0) _shufflePointer = 0;
            }

            int nextPointer = _shufflePointer + 1;
            if (nextPointer < _shuffledIndices.Count)
            {
                _shufflePointer = nextPointer;
                return _shuffledIndices[_shufflePointer];
            }
            else
            {
                // Reached the end of shuffled list
                if (CurrentRepeatMode == RepeatMode.RepeatAll)
                {
                    GenerateShufflePool(totalCount, currentIndex);
                    _shufflePointer = _shuffledIndices.Count > 1 ? 1 : 0;
                    return _shuffledIndices[_shufflePointer];
                }
                else
                {
                    return -1; // Stop playback
                }
            }
        }
        else
        {
            int nextIndex = currentIndex + 1;
            if (nextIndex < totalCount)
            {
                return nextIndex;
            }
            else
            {
                if (CurrentRepeatMode == RepeatMode.RepeatAll)
                {
                    return 0; // Wrap to start
                }
                else
                {
                    return -1; // Stop playback
                }
            }
        }
    }

    public int GetPreviousIndex(int currentIndex, int totalCount, bool isManualSkip = false)
    {
        if (totalCount <= 0) return -1;

        if (CurrentRepeatMode == RepeatMode.RepeatOne && !isManualSkip)
        {
            return currentIndex >= 0 ? currentIndex : 0;
        }

        if (IsShuffleEnabled)
        {
            if (_shuffledIndices.Count != totalCount)
            {
                GenerateShufflePool(totalCount, currentIndex);
            }

            if (_shufflePointer <= 0)
            {
                _shufflePointer = _shuffledIndices.IndexOf(currentIndex);
                if (_shufflePointer < 0) _shufflePointer = 0;
            }

            int prevPointer = _shufflePointer - 1;
            if (prevPointer >= 0 && prevPointer < _shuffledIndices.Count)
            {
                _shufflePointer = prevPointer;
                return _shuffledIndices[_shufflePointer];
            }
            else
            {
                if (CurrentRepeatMode == RepeatMode.RepeatAll)
                {
                    _shufflePointer = _shuffledIndices.Count - 1;
                    return _shuffledIndices[_shufflePointer];
                }
                else
                {
                    return _shuffledIndices.Count > 0 ? _shuffledIndices[0] : 0;
                }
            }
        }
        else
        {
            int prevIndex = currentIndex - 1;
            if (prevIndex >= 0)
            {
                return prevIndex;
            }
            else
            {
                if (CurrentRepeatMode == RepeatMode.RepeatAll)
                {
                    return totalCount - 1;
                }
                else
                {
                    return 0;
                }
            }
        }
    }

    public int ToggleMute(int currentVolume)
    {
        if (IsMuted || currentVolume == 0)
        {
            IsMuted = false;
            int restoredVolume = LastVolumeBeforeMute > 0 ? LastVolumeBeforeMute : 75;
            return restoredVolume;
        }
        else
        {
            LastVolumeBeforeMute = currentVolume;
            IsMuted = true;
            return 0;
        }
    }

    public void OnVolumeChangedByUser(int newVolume)
    {
        if (newVolume > 0)
        {
            LastVolumeBeforeMute = newVolume;
            IsMuted = false;
        }
        else
        {
            IsMuted = true;
        }
    }
}
