using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MonoMod.RuntimeDetour {
    internal static class DetourSorter<T> where T : ISortableDetour {

        public static void Sort(List<T> detours) {
            lock (detours) {
                // OrderBy().ThenBy() fails for no reason on mono and .NET Core,
                // but only on Linux and macOS, not on Windows.

                if (detours.Count <= 1)
                    return;

                detours.Sort(GlobalIndex._);
                Group group = new Group("Init", detours);
                group.Step(BeforeAfterAll._);
                group.Step(BeforeAfter._);
                group.Step(Priority._);
                group.Step(GlobalIndex._);
                detours.Clear();
                group.Flatten(detours);
            }
        }

        private sealed class Group {
            public readonly string StepName;
            public List<T> Items = new List<T>();
            public List<Group> Children = new List<Group>();
            public List<Group> NonMatching = new List<Group>();

            public Group(string stepName) {
                StepName = stepName;
            }
            public Group(string stepName, List<T> items)
                : this(stepName) {
                Items.AddRange(items);
            }

            public void Step(Step step) {
                if (Children.Count != 0) {
                    foreach (Group other in Children) {
                        other.Step(step);
                    }
                    return;
                }

                if (Items.Count <= 1)
                    return;

                if ((Items.Count == 2 && (step.IsFlat ?? true)) || (step.IsFlat ?? false)) {
                    Items.Sort(step);
                    return;
                }

                string stepName = step.GetType().Name;

                Group group = new Group(stepName, new List<T>() { Items[0] });
                Children.Add(group);

                for (int i = 1; i < Items.Count; i++) {
                    T item = Items[i];

                    if (step.Any(group.Items, item)) {
                        Group groupPrev = group;
                        group = null;

                        if (Children.Count > 1) {
                            foreach (Group otherGroup in Children) {
                                if (otherGroup == groupPrev)
                                    continue;
                                if (step.Any(otherGroup.Items, item) ||
                                    step.Any(otherGroup.NonMatching, item))
                                    continue;
                                group = otherGroup;
                                break;
                            }
                        }

                        if (group == null) {
                            group = new Group(stepName);
                            Children.Add(group);
                            group.NonMatching.Add(groupPrev);
                            groupPrev.NonMatching.Add(group);
                        }
                    }

                    group.Items.Add(item);
                }

                if (Children.Count == 1) {
                    Children.Clear();
                    return;
                }

                Children.Sort(step.ForGroup);
            }

            public void Flatten() {
                if (Children.Count == 0)
                    return;

                Items.Clear();
                Flatten(Items);
            }

            public void Flatten(List<T> total) {
                if (Children.Count == 0) {
                    total.AddRange(Items);
                    return;
                }

                foreach (Group other in Children) {
                    other.Flatten(total);
                }
            }
        }

        private abstract class Step : IComparer<T> {
            public abstract GroupComparer ForGroup { get; }
            public abstract int Compare(T x, T y);

            public virtual bool? IsFlat => null;

            public bool Any(List<T> xlist, T y) {
                foreach (T x in xlist)
                    if (Compare(x, y) != 0)
                        return true;
                return false;
            }

            public bool Any(List<Group> groups, T y) {
                foreach (Group group in groups)
                    if (Any(group.Items, y))
                        return true;
                return false;
            }
        }

        private sealed class GroupComparer : IComparer<Group> {
            public Step Step;
            public GroupComparer(Step step) {
                Step = step;
            }
            public int Compare(Group xg, Group yg) {
                int d;
                foreach (T x in xg.Items)
                    foreach (T y in yg.Items)
                        if ((d = Step.Compare(x, y)) != 0)
                            return d;
                return 0;
            }
        }

        private sealed class BeforeAfterAll : Step {
            public static readonly BeforeAfterAll _ = new BeforeAfterAll();
            public static readonly GroupComparer Group = new GroupComparer(_);
            public override GroupComparer ForGroup => Group;
            public override bool? IsFlat => false;
            public override int Compare(T a, T b) {
                if (a.Before.Contains("*") && !b.Before.Contains("*"))
                    return -1;
                if (!a.Before.Contains("*") && b.Before.Contains("*"))
                    return 1;
                if (a.After.Contains("*") && !b.After.Contains("*"))
                    return 1;
                if (!a.After.Contains("*") && b.After.Contains("*"))
                    return -1;

                return 0;
            }
        }

        private sealed class BeforeAfter : Step {
            public static readonly BeforeAfter _ = new BeforeAfter();
            public static readonly GroupComparer Group = new GroupComparer(_);
            public override GroupComparer ForGroup => Group;
            public override int Compare(T a, T b) {
                if (a.Before.Contains(b.ID))
                    return -1;
                if (a.After.Contains(b.ID))
                    return 1;

                if (b.Before.Contains(a.ID))
                    return 1;
                if (b.After.Contains(a.ID))
                    return -1;

                return 0;
            }
        }

        private sealed class Priority : Step {
            public static readonly Priority _ = new Priority();
            public static readonly GroupComparer Group = new GroupComparer(_);
            public override GroupComparer ForGroup => Group;
            public override int Compare(T a, T b) {
                int delta = a.Priority - b.Priority;
                if (delta != 0)
                    return delta;

                return 0;
            }
        }

        private sealed class GlobalIndex : Step {
            public static readonly GlobalIndex _ = new GlobalIndex();
            public static readonly GroupComparer Group = new GroupComparer(_);
            public override GroupComparer ForGroup => Group;
            public override int Compare(T a, T b) {
                return a.GlobalIndex.CompareTo(b.GlobalIndex);
            }
        }

    }
}
