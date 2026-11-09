using System;
using System.Collections.Generic;

namespace ImpossibleLab.Core
{
    /// <summary>仕様23.1 の状態。</summary>
    public enum LabState
    {
        Idle, Drafting, GeneratingDesign, AwaitingApproval, Building, Editing,
        ConfiguringTest, RunningTest, ReviewingResult, Saved, Gallery, Error, Cancelled,
    }

    public interface IAppStateMachine
    {
        LabState State { get; }
        bool Busy { get; }
        bool Transition(LabState next);
        event Action<LabState, LabState> Changed;
    }

    /// <summary>UI状態の一元管理。許可されない遷移は拒否し、非同期処理中の二重操作を防ぐ。</summary>
    public sealed class AppStateMachine : IAppStateMachine
    {
        static readonly Dictionary<LabState, LabState[]> Allowed = new Dictionary<LabState, LabState[]>
        {
            [LabState.Idle] = new[] { LabState.Drafting, LabState.Gallery },
            [LabState.Drafting] = new[] { LabState.GeneratingDesign, LabState.Idle, LabState.Gallery },
            [LabState.GeneratingDesign] = new[] { LabState.AwaitingApproval },
            [LabState.AwaitingApproval] = new[] { LabState.Building, LabState.Drafting, LabState.GeneratingDesign },
            [LabState.Building] = new[] { LabState.Editing },
            [LabState.Editing] = new[] { LabState.ConfiguringTest, LabState.Saved, LabState.Drafting, LabState.Gallery, LabState.GeneratingDesign },
            [LabState.ConfiguringTest] = new[] { LabState.RunningTest, LabState.Editing },
            [LabState.RunningTest] = new[] { LabState.ReviewingResult },
            [LabState.ReviewingResult] = new[] { LabState.Editing, LabState.Saved, LabState.ConfiguringTest },
            [LabState.Saved] = new[] { LabState.Editing, LabState.Gallery, LabState.Drafting },
            [LabState.Gallery] = new[] { LabState.Editing, LabState.Drafting, LabState.Idle },
            [LabState.Error] = new[] { LabState.Idle, LabState.Drafting, LabState.Editing, LabState.Gallery },
            [LabState.Cancelled] = new[] { LabState.Idle, LabState.Drafting, LabState.Editing, LabState.ConfiguringTest },
        };

        public LabState State { get; private set; } = LabState.Idle;
        /// <summary>この状態中は他の操作を受け付けない。</summary>
        public bool Busy => State == LabState.GeneratingDesign || State == LabState.Building || State == LabState.RunningTest;
        public event Action<LabState, LabState> Changed;
        /// <summary>直前の状態（エラーから戻る先）。</summary>
        public LabState Previous { get; private set; } = LabState.Idle;

        public bool CanTransition(LabState next) =>
            next == LabState.Error || next == LabState.Cancelled || (Allowed.TryGetValue(State, out var a) && Array.IndexOf(a, next) >= 0);

        public bool Transition(LabState next)
        {
            if (!CanTransition(next)) return false;
            var prev = State;
            Previous = prev;
            State = next;
            Changed?.Invoke(prev, next);
            return true;
        }
    }
}
