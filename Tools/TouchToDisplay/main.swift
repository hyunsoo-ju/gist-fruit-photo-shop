// TouchToDisplay: sends a USB touch overlay's taps to one chosen display instead of the main one.
//
// Many touch overlays tell macOS they are an absolute pointer (a mouse whose position is the whole screen), and macOS
// then maps them onto the *main* display. This helper takes the overlay over exclusively (IOHIDManager, seize), reads
// its raw X/Y and button, and posts ordinary mouse events at the same spot on the target display (by default the first
// external one), so taps land under the finger on that screen. Quit with Ctrl-C; the overlay goes back to normal.
//
//   swiftc -O Tools/TouchToDisplay/main.swift -o Builds/TouchToDisplay
//   Builds/TouchToDisplay --list                  # displays and HID pointer devices
//   Builds/TouchToDisplay                         # overlay 0x08d3:0x1000 → first external display
//   Builds/TouchToDisplay --vendor 0x08d3 --product 0x1000 --display 2
//
// Needs Input Monitoring (to take the device over) and Accessibility (to post clicks) for the app that runs it
// (Terminal), under System Settings → Privacy & Security.

import Foundation
import IOKit.hid
import CoreGraphics

var vendor = 0x08d3, product = 0x1000
var displayIndex: Int? = nil // 1-based, in --list order
var listOnly = false, verbose = false

var args = Array(CommandLine.arguments.dropFirst())
func intArg(_ s: String) -> Int { s.hasPrefix("0x") ? Int(s.dropFirst(2), radix: 16)! : Int(s)! }
while !args.isEmpty {
    let a = args.removeFirst()
    switch a {
    case "--list": listOnly = true
    case "--verbose", "-v": verbose = true
    case "--vendor": vendor = intArg(args.removeFirst())
    case "--product": product = intArg(args.removeFirst())
    case "--display": displayIndex = intArg(args.removeFirst())
    default: print("Unknown option \(a)"); exit(2)
    }
}

func displays() -> [CGDirectDisplayID] {
    var count: UInt32 = 0
    CGGetActiveDisplayList(0, nil, &count)
    var ids = [CGDirectDisplayID](repeating: 0, count: Int(count))
    CGGetActiveDisplayList(count, &ids, &count)
    return ids
}

func describe(_ id: CGDirectDisplayID) -> String {
    let b = CGDisplayBounds(id)
    return "id \(id) \(Int(b.width))×\(Int(b.height)) at (\(Int(b.minX)), \(Int(b.minY)))"
        + (CGDisplayIsBuiltin(id) != 0 ? " built-in" : " external") + (CGDisplayIsMain(id) != 0 ? ", main" : "")
}

/// The display taps go to: --display N, else the first external one, else the main one.
func targetDisplay() -> CGDirectDisplayID {
    let ids = displays()
    if let n = displayIndex, n >= 1, n <= ids.count { return ids[n - 1] }
    return ids.first { CGDisplayIsBuiltin($0) == 0 } ?? CGMainDisplayID()
}

let manager = IOHIDManagerCreate(kCFAllocatorDefault, IOOptionBits(kIOHIDOptionsTypeNone))

if listOnly {
    print("Displays:")
    for (i, id) in displays().enumerated() { print("  \(i + 1). \(describe(id))") }
    IOHIDManagerSetDeviceMatchingMultiple(manager, [kHIDUsage_GD_Mouse, kHIDUsage_GD_Pointer].map {
        [kIOHIDDeviceUsagePageKey: kHIDPage_GenericDesktop, kIOHIDDeviceUsageKey: $0] as CFDictionary } as CFArray)
    IOHIDManagerOpen(manager, IOOptionBits(kIOHIDOptionsTypeNone))
    print("Pointer devices:")
    for dev in (IOHIDManagerCopyDevices(manager) as? Set<IOHIDDevice>) ?? [] {
        let v = IOHIDDeviceGetProperty(dev, kIOHIDVendorIDKey as CFString) as? Int ?? 0
        let p = IOHIDDeviceGetProperty(dev, kIOHIDProductIDKey as CFString) as? Int ?? 0
        let name = IOHIDDeviceGetProperty(dev, kIOHIDProductKey as CFString) as? String ?? "?"
        print(String(format: "  0x%04x:0x%04x  %@", v, p, name))
        for case let e as IOHIDElement in (IOHIDDeviceCopyMatchingElements(dev, nil, 0) as? [Any]) ?? [] {
            let page = IOHIDElementGetUsagePage(e), usage = IOHIDElementGetUsage(e)
            guard IOHIDElementGetType(e).rawValue <= 3, page == 1 || page == 9 || page == 0x0d else { continue }
            print(String(format: "      page 0x%02x usage 0x%02x  logical %d…%d", page, usage,
                         IOHIDElementGetLogicalMin(e), IOHIDElementGetLogicalMax(e)))
        }
    }
    exit(0)
}

IOHIDManagerSetDeviceMatching(manager, [kIOHIDVendorIDKey: vendor, kIOHIDProductIDKey: product] as CFDictionary)
let result = IOHIDManagerOpen(manager, IOOptionBits(kIOHIDOptionsTypeSeizeDevice))
guard result == kIOReturnSuccess else {
    print(String(format: "Couldn't take over the touch device 0x%04x:0x%04x (IOReturn 0x%08x).", vendor, product, result))
    print("Give the app running this Input Monitoring in System Settings → Privacy & Security, and check it's plugged in.")
    exit(1)
}
let found = (IOHIDManagerCopyDevices(manager) as? Set<IOHIDDevice>)?.count ?? 0
print(String(format: "Touch device 0x%04x:0x%04x: %d found, taken over.", vendor, product, found))
print("Taps go to display \(describe(targetDisplay())). Ctrl-C to stop.")

// Latest normalised position (0…1) and whether the finger is down.
var nx = 0.5, ny = 0.5, down = false

func post(_ type: CGEventType) {
    let b = CGDisplayBounds(targetDisplay())
    let p = CGPoint(x: b.minX + nx * (b.width - 1), y: b.minY + ny * (b.height - 1))
    guard let e = CGEvent(mouseEventSource: nil, mouseType: type, mouseCursorPosition: p, mouseButton: .left) else { return }
    e.setIntegerValueField(.mouseEventClickState, value: 1)
    e.post(tap: .cghidEventTap)
    if verbose { print("\(type.rawValue) at (\(Int(p.x)), \(Int(p.y)))") }
}

let callback: IOHIDValueCallback = { _, _, _, value in
    let e = IOHIDValueGetElement(value)
    let page = IOHIDElementGetUsagePage(e), usage = IOHIDElementGetUsage(e)
    let v = IOHIDValueGetIntegerValue(value)
    let lo = Double(IOHIDElementGetLogicalMin(e)), hi = Double(IOHIDElementGetLogicalMax(e))
    func norm() -> Double { hi > lo ? min(1, max(0, (Double(v) - lo) / (hi - lo))) : 0 }

    if page == kHIDPage_GenericDesktop && usage == kHIDUsage_GD_X { nx = norm() }
    else if page == kHIDPage_GenericDesktop && usage == kHIDUsage_GD_Y { ny = norm() }
    else if (page == kHIDPage_Button && usage == 1) || (page == kHIDPage_Digitizer && usage == kHIDUsage_Dig_TipSwitch) {
        let now = v != 0
        if now != down {
            down = now
            post(.mouseMoved)
            post(now ? .leftMouseDown : .leftMouseUp)
        }
        return
    } else { return }
    post(down ? .leftMouseDragged : .mouseMoved)
}
IOHIDManagerRegisterInputValueCallback(manager, callback, nil)
IOHIDManagerScheduleWithRunLoop(manager, CFRunLoopGetMain(), CFRunLoopMode.defaultMode.rawValue)

signal(SIGINT) { _ in print("\nStopped; the touch device is back to normal."); exit(0) }
CFRunLoopRun()
