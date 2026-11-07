using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SpaceCanvas.ProjectIO.XR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

namespace SpaceCanvas.ProjectIO.Editor.XR.Tests
{
    /// <summary>Regression for a wrist-only hand device masking the controller/pinch profile.</summary>
    public sealed class OpenXRPointerTests
    {
        private readonly List<InputDevice> devices = new List<InputDevice>();
        private GameObject host;
        private SpaceCanvasOpenXRPointer pointer;
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        [SetUp]
        public void Setup()
        {
            InputSystem.RegisterLayout("{\"name\":\"SpaceCanvasTestPose\",\"extend\":\"XRController\"}");
            InputSystem.RegisterLayout("{\"name\":\"SpaceCanvasTestClick\",\"extend\":\"XRController\",\"controls\":[{\"name\":\"triggerPressed\",\"layout\":\"Button\"}]}");
            InputSystem.RegisterLayout("{\"name\":\"SpaceCanvasTestHand\",\"extend\":\"XRController\",\"controls\":[{\"name\":\"pointerActivated\",\"layout\":\"Button\"},{\"name\":\"pinchValue\",\"layout\":\"Axis\"}]}");
            InputSystem.RegisterLayout("{\"name\":\"SpaceCanvasTestAimHand\",\"extend\":\"TrackedDevice\",\"controls\":[{\"name\":\"pointerActivated\",\"layout\":\"Button\"},{\"name\":\"pointerPosition\",\"layout\":\"Vector3\"},{\"name\":\"pointerRotation\",\"layout\":\"Quaternion\"},{\"name\":\"aimTrackingState\",\"layout\":\"Integer\"}]}");
            host = new GameObject("OpenXR pointer regression");
            host.SetActive(false);
            pointer = host.AddComponent<SpaceCanvasOpenXRPointer>();
        }

        private InputDevice Add(string layout, bool tracked)
        {
            var device = InputSystem.AddDevice(layout);
            devices.Add(device);
            InputSystem.SetDeviceUsage(device, "LeftHand");
            SetTracked(device, tracked);
            return device;
        }

        private static void SetTracked(InputDevice device, bool tracked)
        {
            using (StateEvent.From(device, out var state))
            {
                device.GetChildControl<ButtonControl>("isTracked").WriteValueIntoEvent(tracked ? 1f : 0f, state);
                device.GetChildControl<QuaternionControl>("deviceRotation").WriteValueIntoEvent(Quaternion.identity, state);
                InputState.Change(device, state);
            }
        }

        [TearDown]
        public void Cleanup()
        {
            Object.DestroyImmediate(host);
            foreach (var device in devices) InputSystem.RemoveDevice(device);
            devices.Clear();
            InputSystem.RemoveLayout("SpaceCanvasTestClick");
            InputSystem.RemoveLayout("SpaceCanvasTestPose");
            InputSystem.RemoveLayout("SpaceCanvasTestHand");
            InputSystem.RemoveLayout("SpaceCanvasTestAimHand");
        }

        [Test]
        public void TrackedWristCannotMaskClickableController()
        {
            pointer.leftHand = true;
            var wrist = Add("SpaceCanvasTestPose", true);
            var controller = Add("SpaceCanvasTestClick", true);
            // Reproduce the real device state: the wrist was previously selected.
            typeof(SpaceCanvasOpenXRPointer).GetField("deviceId", PrivateInstance).SetValue(pointer, wrist.deviceId);
            var chosen = typeof(SpaceCanvasOpenXRPointer).GetMethod("FindDevice", PrivateInstance).Invoke(pointer, null);
            Assert.That(chosen, Is.SameAs(controller));
            SetTracked(controller, false);
            chosen = typeof(SpaceCanvasOpenXRPointer).GetMethod("FindDevice", PrivateInstance).Invoke(pointer, null);
            Assert.That(chosen, Is.Not.SameAs(wrist), "A pose-only device cannot click even when tracked.");
        }

        [Test]
        public void TrackingLossReleasesUIPress()
        {
            var reader = new XRInputButtonReader("Test", inputSourceMode: XRInputButtonReader.InputSourceMode.ManualValue);
            typeof(SpaceCanvasOpenXRPointer).GetField("uiPress", PrivateInstance).SetValue(pointer, reader);
            typeof(SpaceCanvasOpenXRPointer).GetMethod("DeliverUIPress", PrivateInstance).Invoke(pointer, new object[] {true});
            Assert.That(reader.ReadIsPerformed(), Is.True);
            typeof(SpaceCanvasOpenXRPointer).GetMethod("LoseTracking", PrivateInstance).Invoke(pointer, null);
            Assert.That(reader.ReadIsPerformed(), Is.False);
            Assert.That(reader.ReadWasCompletedThisFrame(), Is.True);
        }

        [Test]
        public void NativeHandActivationReleasesEvenIfPinchStrengthRemainsHigh()
        {
            var hand = Add("SpaceCanvasTestHand", true);
            var read = typeof(SpaceCanvasOpenXRPointer).GetMethod("ReadPress", BindingFlags.NonPublic | BindingFlags.Static);
            using (StateEvent.From(hand, out var state))
            {
                hand.GetChildControl<AxisControl>("pinchValue").WriteValueIntoEvent(1f, state);
                hand.GetChildControl<ButtonControl>("pointerActivated").WriteValueIntoEvent(1f, state);
                InputState.Change(hand, state);
            }
            Assert.That(read.Invoke(null, new object[] {hand, false}), Is.True);
            using (StateEvent.From(hand, out var state))
            {
                hand.GetChildControl<ButtonControl>("pointerActivated").WriteValueIntoEvent(0f, state);
                InputState.Change(hand, state);
            }
            Assert.That(read.Invoke(null, new object[] {hand, true}), Is.False,
                "The completed native pinch must release UI instead of latching on its continuous strength.");
        }

        [Test]
        public void TrackedWristWithLostAimCannotMoveOrConfirmPlacement()
        {
            pointer.leftHand = true;
            var hand = Add("SpaceCanvasTestAimHand", true);
            SetAim(hand, 3);
            var find = typeof(SpaceCanvasOpenXRPointer).GetMethod("FindDevice", PrivateInstance);
            Assert.That(find.Invoke(pointer, null), Is.SameAs(hand));
            // XR Hands resets an unavailable aim to (zero, identity), while wrist stays tracked.
            SetAim(hand, 0);
            Assert.That(find.Invoke(pointer, null), Is.Null);
            var controller = Add("SpaceCanvasTestClick", true);
            Assert.That(find.Invoke(pointer, null), Is.SameAs(controller));
            SetTracked(controller, false);
            SetAim(hand, 1); // A position without a valid rotation is also unsafe.
            Assert.That(find.Invoke(pointer, null), Is.Null);
        }

        [Test]
        public void NativeProfileWinsOverDuplicateSynthesizedHandEvenWhenHandWasCurrent()
        {
            pointer.leftHand = true;
            var hand = Add("SpaceCanvasTestAimHand", true); SetAim(hand, 3);
            var native = Add("SpaceCanvasTestHand", true);
            typeof(SpaceCanvasOpenXRPointer).GetField("deviceId", PrivateInstance).SetValue(pointer, hand.deviceId);
            Assert.That(typeof(SpaceCanvasOpenXRPointer).GetMethod("FindDevice", PrivateInstance).Invoke(pointer, null), Is.SameAs(native));
            SetTracked(native, false);
            Assert.That(typeof(SpaceCanvasOpenXRPointer).GetMethod("FindDevice", PrivateInstance).Invoke(pointer, null), Is.SameAs(hand));
        }

        private static void SetAim(InputDevice device, int trackingState)
        {
            using (StateEvent.From(device, out var state))
            {
                device.GetChildControl<QuaternionControl>("pointerRotation").WriteValueIntoEvent(Quaternion.identity, state);
                device.GetChildControl<IntegerControl>("aimTrackingState").WriteValueIntoEvent(trackingState, state);
                InputState.Change(device, state);
            }
        }
    }
}
