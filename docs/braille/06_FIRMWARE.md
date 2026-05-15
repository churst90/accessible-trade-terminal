# 06 — Firmware architecture

This document specifies the firmware that drives the full-array display. The 40-cell prototype uses a simplified version (one slave); full-array uses one master + six slaves coordinated over SPI.

Reference firmware skeleton lives in `docs/braille/firmware/` (separate task; not all files are filled in V1 of this doc set).

---

## 1. Hardware mapping

### 1.1 MCU layout

| Role | MCU | Quantity | Responsibility |
|---|---|---|---|
| Master | RP2350 (Pi Pico 2) | 1 | USB host interface, framebuffer ownership, command dispatch to slaves, status reporting |
| Slave | RP2040 | 6 | Drive 4 banks each (24 banks total). PIO pulse generation, bank-local diff refresh, reports completion to master |

### 1.2 Bank assignments

Each slave owns 4 banks. Each bank is 160 coils. So one slave drives 640 coils; six slaves drive the full 3,840.

Bank-to-slave map:
| Slave | Banks | Pin range |
|---|---|---|
| 0 | 0, 1, 2, 3 | 0 – 639 |
| 1 | 4, 5, 6, 7 | 640 – 1,279 |
| 2 | 8, 9, 10, 11 | 1,280 – 1,919 |
| 3 | 12, 13, 14, 15 | 1,920 – 2,559 |
| 4 | 16, 17, 18, 19 | 2,560 – 3,199 |
| 5 | 20, 21, 22, 23 | 3,200 – 3,839 |

### 1.3 GPIO map (slave)

Per slave, per bank:
- 4 GPIO pins for row mux SEL (4-bit, selects one of 16 rows)
- 7 GPIO pins for column mux SEL (3-bit + EN, selects one of 10 columns within bank)

Wait — re-checking. 160 coils per bank = 16 rows × 10 cols. Need 4 row bits + 4 col bits (since 10 cols < 16). So 8 SEL pins per bank.

Plus 2 H-bridge pins (IN1, IN2).
Plus 1 enable / fault status.

That's 11 pins per bank × 4 banks = 44 pins per slave. RP2040 has 26 usable GPIOs. **Insufficient for direct-drive.**

Solution: share row/col SEL across banks. All 4 banks within a slave share the same row+col addressing pins (8 pins total) and use bank-enable lines (4 pins) to select which bank's H-bridge fires. Total: 8 + 4 + 2 (H-bridge) + 1 (status) = **15 pins per slave**, well within 26.

This works because we never fire two banks within a slave on the same pulse (would double the slave's peak current). Banks within a slave fire sequentially at ~10 ms apart; banks across different slaves fire simultaneously.

### 1.4 SPI between master and slaves

Master uses 1 SPI bus + 6 chip-select lines (one per slave).
Each slave: 4 SPI pins (MOSI, MISO, SCK, CS) + 1 ready/done line back to master.

Total master GPIO: 4 (SPI) + 6 (CS) + 6 (slave done) + USB + power = ~18 pins. Fine on RP2350 (30 GPIOs).

---

## 2. Communication protocol

### 2.1 Master ↔ slave (SPI, 8 MHz)

Master sends a command word, slave responds with status:

```
Master → Slave:
  Byte 0: Command opcode
  Byte 1-N: Payload

Slave → Master (in next transaction):
  Byte 0: Status (0 = OK, non-zero = error)
  Byte 1-N: Result payload (if any)
```

Opcodes:
| Opcode | Name | Payload | Action |
|---|---|---|---|
| 0x01 | SET_BANK_FRAMEBUFFER | 80 bytes (160 bits / 8) | Replace target framebuffer for one bank |
| 0x02 | DIFF_REFRESH_BANK | 1 byte (bank ID) | Compare target vs current; flip changed pins |
| 0x03 | FULL_REFRESH_BANK | 1 byte | Drop all, raise selected |
| 0x04 | GET_STATUS | — | Returns dead-pin map, temperature, last-error |
| 0x05 | SET_PULSE_WIDTH | 1 byte (ms) | Reconfigure default pulse |
| 0x06 | SET_PULSE_CURRENT | 1 byte (DAC value) | Trim per-bank pulse strength |
| 0x07 | TEST_PIN | 2 bytes (col, row) | Single-pin flip for diagnostics |

### 2.2 Master ↔ host (USB, two interfaces)

**Interface 1: USB HID Braille (Usage Page 0x0041, Usage 0x0001)**
- Standard Windows screen-reader compatible.
- Output reports: 8-dot cell data, ~32 cells × 8 dots = 256 bits per row.
- 8 output reports = 2,048 bits = 256 bytes. Matches the 53% accessibility ceiling Tyler measured on Monarch.

**Interface 2: USB Vendor-specific (custom protocol for AccessibleTrader and similar)**
- Vendor-specific HID collection or USB CDC class.
- Allows raw framebuffer write at full hardware resolution.
- Provides HID Braille translation pass-through.

The vendor interface is what AccessibleTrader will use to drive the full pin matrix.

### 2.3 Cell rendering and the uniform-pitch convention

**The hardware uses uniform 2.5 mm pitch on both axes.** This enables tactile graphics at full Monarch-class density. Text rendering inserts blank pin columns between cells in firmware so a reader feels the inter-cell gap that distinguishes one letter from the next.

**Text render mode** (used when host sends braille text, or HID Braille output reports):

For each braille cell:
- Allocate 3 hardware pin columns: 2 dot columns + 1 blank column to the right.
- Map cell's dot-1 (top-left) → (col_offset + 0, row 0)
- Map cell's dot-2 (mid-left) → (col_offset + 0, row 1)
- Map cell's dot-3 (bot-left) → (col_offset + 0, row 2)
- Map cell's dot-7 (lower-left, 8-dot only) → (col_offset + 0, row 3)
- Map cell's dot-4 (top-right) → (col_offset + 1, row 0)
- Map cell's dot-5 (mid-right) → (col_offset + 1, row 1)
- Map cell's dot-6 (bot-right) → (col_offset + 1, row 2)
- Map cell's dot-8 (lower-right, 8-dot only) → (col_offset + 1, row 3)
- Force pins at (col_offset + 2, all rows) to DOWN (the inter-cell blank column).

The `col_offset` advances by 3 per cell. So cell N occupies hardware columns 3N, 3N+1, 3N+2.

**Inter-cell gap:** 2.5 mm (one blank column at uniform pitch). ISO 17049 standard is 3.5 mm; our 2.5 mm is slightly tighter but reads correctly at normal pace per Monarch's empirical results.

**Text-mode capacity per tier:**

| Tier | Hardware columns | Cells in text mode | Hardware rows | Lines of text |
|---|---|---|---|---|
| Single-cell | 2 (no inter-cell gap fits in 2 cols) | reads as 1 cell only | 4 | 1 |
| Forty-cell line | 120 | 40 | 4 | 1 |
| Full-array | 60 | 20 cells/line | 64 | 16 lines |

**Graphics render mode** (used when host sends raw framebuffer or vendor-specific commands):
- All hardware pins addressed individually.
- No blank-column convention; full pixel control.
- Tier capacities: single-cell 8 px; forty-cell 480 px line strip; full-array 3,840 px (60 × 64).

Both modes can be active in different regions of the framebuffer simultaneously — for example, a chart at the top of the array and a label of braille text at the bottom.

### 2.3 Vendor protocol (USB CDC, framing)

Two layers: a binary frame protocol for raw graphics, and a text protocol for diagnostics.

**Frame protocol (binary):**
```
Header:
  Byte 0: Magic 0xA5
  Byte 1: Version (0x01)
  Byte 2: Command
  Byte 3-4: Payload length (LE u16)

Payload:
  variable bytes per command

Commands:
  0x10: SET_FRAMEBUFFER      payload: 480 bytes (3,840 bits)
  0x11: REFRESH              payload: 1 byte (mode: 0=diff, 1=full)
  0x12: GET_STATUS           payload: -
  0x13: SELF_TEST            payload: 1 byte (test ID)
  0x14: SET_REGION           payload: 5 bytes (x, y, w, h) + (w*h+7)/8 bytes
```

**Diagnostic text protocol** (also USB CDC, parallel channel or interleaved): plain ASCII commands, useful for command-line tools and bringup.

---

## 3. Master firmware (RP2350)

```c
// Pseudo-C; real implementation in firmware/master/main.c

#define BANK_COUNT 24
#define BANK_PIN_COUNT 160
#define TOTAL_PIN_COUNT 3840
#define FRAMEBUFFER_BYTES 480

uint8_t framebuffer[FRAMEBUFFER_BYTES];
uint8_t displayed[FRAMEBUFFER_BYTES];

// Scatter target framebuffer to slaves
void distribute_framebuffer() {
    for (int slave = 0; slave < 6; slave++) {
        for (int bank_idx = 0; bank_idx < 4; bank_idx++) {
            int bank_id = slave * 4 + bank_idx;
            int byte_offset = bank_id * 20; // 160 bits / 8
            spi_send_to_slave(slave, CMD_SET_BANK_FB, &framebuffer[byte_offset], 20);
        }
    }
}

void refresh_all_banks(refresh_mode_t mode) {
    for (int slave = 0; slave < 6; slave++) {
        for (int bank_idx = 0; bank_idx < 4; bank_idx++) {
            spi_send_to_slave(slave, mode == DIFF ? CMD_DIFF_REFRESH : CMD_FULL_REFRESH, &bank_idx, 1);
        }
    }
    // Wait for all done lines
    while (any_slave_busy()) tight_loop_contents();
    // Update displayed buffer
    memcpy(displayed, framebuffer, FRAMEBUFFER_BYTES);
}
```

### 3.1 USB HID Braille handler

Standard TinyUSB HID Braille descriptor. On output report received:
- Translate 8-dot cells to pin matrix bits.
- Apply HID-Braille-to-pin mapping (see [`docs/EMAIL_APH_WILLOW_FREE.md`](../EMAIL_APH_WILLOW_FREE.md) for the cell-to-pin layout discussion).
- Update framebuffer.
- Trigger diff refresh.

### 3.2 Vendor protocol handler

Parse incoming binary frames over USB CDC. On SET_FRAMEBUFFER:
- Copy payload to framebuffer.
- Trigger diff refresh.

### 3.3 Self-test on power-up

1. Drive all pins down (full-refresh with empty framebuffer).
2. Drive all pins up.
3. Check slave status; build dead-pin map.
4. Save dead-pin map to flash.
5. Render power-up indicator pattern (e.g., "READY" in braille on one row).

### 3.4 Watchdog

Hardware watchdog set to 100 ms. Refresh in main loop. If main loop hangs, MCU resets — preventing any coil from being stuck on indefinitely.

---

## 4. Slave firmware (RP2040)

Critical-path code runs on PIO state machines; CPU only manages framebuffer and SPI.

### 4.1 PIO program for bank pulse

```pio
; Pulse a single coil via H-bridge, polarity selected by direction bit
.program flip_pulse
    ; Input: side-set pin = H-bridge IN1, pin = H-bridge IN2
    ; Y register holds pulse cycle count

    pull noblock                ; Get target direction (0 = down, 1 = up)
    out x, 1                    ; X = direction
    set pindirs, 0b11           ; Both IN1, IN2 outputs

    ; Set IN1, IN2 based on direction
    jmp x-- direction_set       ; If direction=1, fall through to "up" branch
    set pins, 0b10              ; IN1=1, IN2=0 → flip up
    jmp pulse_active
direction_set:
    set pins, 0b01              ; IN1=0, IN2=1 → flip down
pulse_active:

    ; Hold pulse for X cycles
    mov y, isr
pulse_loop:
    nop                         ; 1 cycle
    jmp y-- pulse_loop          ; 1 cycle = 2 cycles per loop iter

    ; End pulse
    set pins, 0b00              ; IN1=0, IN2=0 → idle
```

This PIO program completes a pulse in fixed time independent of CPU state. **The watchdog is in hardware — even a CPU lockup cannot extend the pulse.**

### 4.2 Diff-refresh inner loop

```c
// Pseudo-C; real implementation in firmware/slave/refresh.c

uint8_t bank_target[20];
uint8_t bank_displayed[20];

void diff_refresh_bank(uint8_t bank_id) {
    for (int byte_idx = 0; byte_idx < 20; byte_idx++) {
        uint8_t diff = bank_target[byte_idx] ^ bank_displayed[byte_idx];
        for (int bit = 0; bit < 8; bit++) {
            if ((diff >> bit) & 1) {
                int pin_idx = byte_idx * 8 + bit;
                int row = pin_idx / 10;  // 16 rows
                int col = pin_idx % 10;  // 10 cols
                bool target_up = (bank_target[byte_idx] >> bit) & 1;
                
                set_row_col(row, col);
                enable_bank(bank_id);
                trigger_pio_pulse(target_up);
                wait_pio_done();
                disable_bank(bank_id);
            }
        }
    }
    memcpy(bank_displayed, bank_target, 20);
}
```

### 4.3 Pulse sequencing rule

**No two pins flipped in adjacent cycles.** This avoids the dynamic-crosstalk worst case. The slave's diff-refresh loop natively serializes within a bank, but the order matters:

Adopted ordering: for each bank, iterate rows 0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15. This ensures the first row is finished before the row 1 cell above it flips, etc. — interlocking adjacency.

### 4.4 Bank thermal sensing

Each bank has a TMP235 temperature sensor adjacent to its H-bridge. Slave reads ADC every 100 ms; if any bank reaches 80°C, slave flags master and master throttles refresh rate.

---

## 5. Power management

### 5.1 Burst-current handling

Single bank fires for ~5 ms, drawing ~1.5 A at 5 V = 7.5 W instantaneous. With 24 banks running in parallel: 24 × 7.5 = 180 W peak.

Not all 24 fire simultaneously — sequencing rule means at most 6 banks active at once (one per slave). 6 × 7.5 = 45 W peak instantaneous. With local 470 µF caps per bank and 10,000 µF total bulk:

Bulk cap charge: ½ C V² = ½ × 60 mF × 12² = 4.3 J (at 12 V before buck conversion).

Per pulse: 7.5 W × 5 ms = 37 mJ per coil = 222 mJ for 6 coils.

Bulk caps replenish at supply rate: 50 W ÷ 12 V = 4.2 A continuous, much higher than 0.018 A average burst.

**Power supply is 12V/4A = 48W; comfortable headroom over 45W peak.**

### 5.2 Per-bank buck converter

12V to 5V conversion at the bank avoids running 1.5 A across the driver PCB. TPS56C230 (3 A capable) per bank, with local 470 µF output cap.

### 5.3 USB-C PD alternative

If USB-C PD source provides 5V/5A, can skip 12V supply. Less efficient (more current through PCB traces) but simpler.

V1 spec: 12V barrel jack, optional USB PD support documented but not implemented.

---

## 6. Diagnostics and debugging

### 6.1 Self-test commands (USB CDC)

```
> selftest all     — runs full-array test, reports dead pins
> selftest pin 1234 — flips a single pin 100 times, reports success rate
> selftest bank 5  — exercises one bank
> dump fb           — prints framebuffer contents
> dump dead         — prints dead-pin map
> stats             — total flips, refresh count, uptime, max temp
```

### 6.2 Logging

Slaves report errors via SPI; master accumulates and exposes via diagnostic CDC.

Critical errors:
- Bank temperature > 100°C (auto-shutdown bank)
- Coil pulse fault (DRV8847 nFAULT asserted)
- SPI communication timeout (slave not responding)
- Watchdog reset

---

## 7. Firmware deployment

### 7.1 Build

- Master: `firmware/master/CMakeLists.txt`, builds `master.uf2` with Pico SDK.
- Slave: `firmware/slave/CMakeLists.txt`, builds `slave.uf2`.

### 7.2 Flashing

1. Hold BOOT, press RESET on master Pico 2; appears as USB MSD.
2. Drag `master.uf2` to the drive.
3. Pico reboots and runs.

For slaves: each is on the driver PCB; programming via SWD (3-pin breakout to a Picoprobe) or via UF2 mode through the master once it's running (master implements a slave-bootloader-passthrough command).

### 7.3 Versioning

Firmware version tag in flash. Master returns version on `GET_STATUS`. AccessibleTrader can warn user if firmware mismatch with host driver.

---

## 8. Driver-side (Windows / Linux)

### 8.1 Windows (HID Braille)

Built-in Windows HID Braille driver since Windows 10 1903. No custom driver needed. Screen readers (NVDA, JAWS, Narrator) use the Braille HID interface directly.

### 8.2 Vendor protocol — Windows

WinUSB driver via libusb / hidapi. AccessibleTrader uses HidSharp for HID class or LibUsbDotNet for raw USB.

### 8.3 Linux

`/dev/usb/hiddev*` for HID Braille. udev rule grants user access to vendor protocol via libusb.

---

## 9. Performance targets

| Metric | V1 target | V1 measured (TBD) |
|---|---|---|
| Full refresh (24 banks parallel) | 1.6 s | — |
| Diff refresh (10% pin change) | 200 ms | — |
| HID Braille update latency | <50 ms | — |
| Vendor full-frame latency | <100 ms | — |
| Idle power | <1 W | — |
| Full-refresh peak power | 45 W | — |

---

## 10. Future firmware features (V2+)

- Animation / scrolling primitives (smooth scroll a long line of text without full re-render).
- Tactile rendering hints (anti-alias dot patterns where graphics density is high).
- BLE wireless host (RP2350 has BT support; allows tablet/phone host).
- Multiple framebuffer "pages" with hardware paging.
- User-configurable refresh order for tactile feel (clockwise spiral, line-by-line, bottom-up).

V1 ships text + raw graphics + HID Braille. V2 adds animation. V3+ adds wireless and pages.
