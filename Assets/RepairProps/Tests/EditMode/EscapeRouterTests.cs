using System.Collections.Generic;
using BorderRepair.Controls;
using NUnit.Framework;
using UnityEngine;
using Layer = BorderRepair.Controls.EscapeRouter.Layer;

namespace BorderRepair.RepairProps.Tests
{
    // One press of Esc closes one thing. Each "owner" here behaves like the real handlers: it asks the router, and
    // closes itself if it gets the press, in whatever order the frame happens to run them.
    public sealed class EscapeRouterTests
    {
        readonly List<Object> owners = new List<Object>();
        readonly Dictionary<Layer, bool> open = new Dictionary<Layer, bool>();
        int frame;
        bool key;

        [SetUp]
        public void SetUp()
        {
            EscapeRouter.Reset();
            frame = 0; key = false; open.Clear();
            EscapeRouter.KeySource = () => key;
            EscapeRouter.FrameSource = () => frame;
            foreach (Layer layer in new[] { Layer.Shop, Layer.Menu, Layer.Gesture, Layer.Closeup })
            {
                var owner = new GameObject("EscOwner_" + layer);
                owners.Add(owner);
                open[layer] = false;
                var captured = layer;
                EscapeRouter.Register(layer, owner, () => open[captured]);
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in owners) Object.DestroyImmediate(o);
            owners.Clear();
            EscapeRouter.Reset();
        }

        /// <summary>One frame with Esc down; handlers run in the given order. Returns which layers acted.</summary>
        List<Layer> PressEsc(params Layer[] order)
        {
            frame++; key = true;
            var acted = new List<Layer>();
            foreach (var layer in order)
            {
                bool listening = layer == Layer.Cursor || open[layer];
                if (listening && EscapeRouter.Pressed(layer)) { acted.Add(layer); if (layer != Layer.Cursor) open[layer] = false; }
            }
            frame++; key = false;
            return acted;
        }

        static readonly Layer[] TopDown = { Layer.Shop, Layer.Menu, Layer.Gesture, Layer.Closeup, Layer.Cursor };
        static readonly Layer[] BottomUp = { Layer.Cursor, Layer.Closeup, Layer.Gesture, Layer.Menu, Layer.Shop };

        [Test]
        public void OnePress_ClosesOnlyTheTopmost_WhateverTheUpdateOrder([Values(true, false)] bool topDown)
        {
            var order = topDown ? TopDown : BottomUp;
            open[Layer.Shop] = open[Layer.Gesture] = open[Layer.Closeup] = true;

            CollectionAssert.AreEqual(new[] { Layer.Shop }, PressEsc(order), "the parts terminal closes; the gesture and the close-up stay");
            CollectionAssert.AreEqual(new[] { Layer.Gesture }, PressEsc(order), "the second press cancels the gesture");
            CollectionAssert.AreEqual(new[] { Layer.Closeup }, PressEsc(order), "the third leaves the close-up");
            CollectionAssert.AreEqual(new[] { Layer.Cursor }, PressEsc(order), "with nothing open, Esc lets go of the mouse");
        }

        [Test]
        public void NoPress_NothingActs()
        {
            open[Layer.Shop] = true;
            frame++;
            Assert.IsFalse(EscapeRouter.Pressed(Layer.Shop));
            Assert.IsFalse(EscapeRouter.Pressed(Layer.Cursor));
        }

        [Test]
        public void ADestroyedOwner_NoLongerBlocks()
        {
            open[Layer.Shop] = true;
            Object.DestroyImmediate(owners[0]);   // the shop
            CollectionAssert.AreEqual(new[] { Layer.Cursor }, PressEsc(Layer.Cursor));
        }
    }
}
