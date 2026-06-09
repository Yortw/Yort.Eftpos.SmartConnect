# Yort.Eftpos.SmartConnect

A .NET client library for the **SmartPay / Shift4 SmartConnect** EFTPOS integration (New Zealand) — a cloud REST API that pairs a point-of-sale register to a payment terminal and processes card transactions via an asynchronous polling model.

> ⚠️ **Work in progress.** This library is under active initial development and is not yet released.
>
> **Unofficial:** this is an independent, unofficial client library and is not affiliated with, endorsed by, or supported by Shift4 / SmartPay. "SmartConnect", "SmartPay", and "Shift4" are trademarks of their respective owners.

## What it is

- Targets **.NET Standard 2.0** (consumable by .NET Framework 4.6.1+ through modern .NET).
- Wraps the two SmartConnect endpoints (`PUT /Pairing/{code}` and `POST /Transaction` + GET-poll) behind a small, testable client.
- Treats declines/cancellations as **data** (a result status), and reserves exceptions for failures to obtain an answer.
- Makes crash-recovery a first-class concern: the caller persists the transaction state (including the polling URL) via an injected contract, and can resume polling after a restart.

## Status

See the design and implementation plan (in the consuming product's repository) for the current scope, known vendor limitations, and roadmap.

## Licence

MIT (to be added).
