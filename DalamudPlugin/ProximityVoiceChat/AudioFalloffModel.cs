﻿using System;

namespace ProximityVoiceChat
{
    [Serializable]
    public class AudioFalloffModel
    {
        public enum FalloffType
        {
            None = 0,
            InverseDistance = 1,
            ExponentialDistance = 2,
            LinearDistance = 3,
        }

        public FalloffType Type { get; set; } = FalloffType.InverseDistance;
        public float MinimumDistance { get; set; } = 1.0f;
        public float MaximumDistance { get; set; } = 10.0f;
        public float FalloffFactor { get; set; } = 1.0f;
    }
}