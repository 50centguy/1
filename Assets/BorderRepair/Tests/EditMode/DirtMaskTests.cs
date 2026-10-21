using System;
using System.Linq;
using BorderRepair.Cleaning;
using NUnit.Framework;
using UnityEngine;

namespace BorderRepair.Tests
{
    public sealed class DirtMaskTests
    {
        DirtMaskData Make() => new DirtMaskData(32,Enumerable.Repeat((byte)255,32*32).ToArray());
        [Test] public void BrushOnlyRemovesLocalDirtAndNeverAddsIt()
        {
            var data=Make(); var before=data.Pixels.Select(p=>p.r).ToArray();
            Assert.IsTrue(data.Paint(new Vector2(.5f,.5f),.1f,.4f));
            Assert.Greater(data.Progress,0); Assert.Less(data.Progress,.1f);
            Assert.AreEqual(before[0],data.Pixels[0].r);
            for(int i=0;i<before.Length;i++) Assert.LessOrEqual(data.Pixels[i].r,before[i]);
            float first=data.Progress; data.Paint(new Vector2(.5f,.5f),.1f,.4f); Assert.Greater(data.Progress,first);
        }
        [Test] public void ClearResetAndExportImportAreDeterministic()
        {
            var data=Make(); var initial=data.Pixels.ToArray();
            data.Paint(new Vector2(.3f,.7f),.15f,.7f);
            var saved=data.Export(); var other=Make(); Assert.IsTrue(other.Import(saved));
            Assert.That(other.Progress,Is.EqualTo(data.Progress).Within(.003f));
            data.Clear(); Assert.AreEqual(1,data.Progress); Assert.IsTrue(data.Pixels.All(p=>p.r==0));
            data.Reset(); Assert.AreEqual(0,data.Progress); CollectionAssert.AreEqual(initial,data.Pixels);
            Assert.IsFalse(data.Import("not valid")); CollectionAssert.AreEqual(initial,data.Pixels);
        }
        [Test] public void UnusedUvTexelsAreNotCountedOrPainted()
        {
            var coverage=new byte[32*32]; coverage[16*32+16]=255;
            var data=new DirtMaskData(32,coverage);
            Assert.AreEqual(0,data.Pixels[0].r);
            Assert.IsTrue(data.Paint(new Vector2(.515625f,.515625f),.05f,2));
            Assert.AreEqual(1,data.Progress);
        }
        [Test] public void InvalidBrushCannotCorruptMask()
        {
            var data=Make(); var original=data.Export();
            Assert.IsFalse(data.Paint(new Vector2(float.NaN,0),.1f,1));
            Assert.IsFalse(data.Paint(Vector2.zero,-1,1));
            Assert.IsFalse(data.Paint(Vector2.zero,.1f,float.PositiveInfinity));
            Assert.AreEqual(original,data.Export());
            Assert.Throws<ArgumentException>(()=>new DirtMaskData(32,new byte[1]));
        }
        [Test] public void AtlasEdgeDoesNotWrapToTheOppositeSide()
        {
            var data=Make(); byte opposite=data.Pixels[31].r;
            data.Paint(new Vector2(.01f,.01f),.1f,1);
            Assert.AreEqual(opposite,data.Pixels[31].r);
            Assert.IsFalse(data.Paint(new Vector2(1.2f,0),.1f,1));
        }
        [Test] public void BrushCannotBleedAcrossSeparatedUvIslands()
        {
            var coverage=Enumerable.Repeat((byte)255,32*32).ToArray();
            for(int y=0;y<32;y++) coverage[y*32+16]=0;
            var data=new DirtMaskData(32,coverage); byte right=data.Pixels[16*32+18].r;
            Assert.IsTrue(data.Paint(new Vector2(.45f,.5f),.2f,1,true));
            Assert.AreEqual(right,data.Pixels[16*32+18].r);
            Assert.IsFalse(data.SameIsland(new Vector2(.45f,.5f),new Vector2(.58f,.5f)));
        }
    }
}
