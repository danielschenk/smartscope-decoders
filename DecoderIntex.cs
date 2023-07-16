using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using LabNation.Interfaces;

namespace LabNation.Decoders
{
    // Decoder for the pulse-length encoded, asynchronous button and LED messages
    // of the Intex ECO5220G saltwater system's UI board.
    // Possibly works for UI boards of pumps too (to be investigated).
    // Inspired by Decoder1Wire.cs from the LabNation decoders.
    [Export(typeof(IProcessor))]
    public class DecoderIntex : IDecoder
    {
        public DecoderDescription Description
        {
            get
            {
                return new DecoderDescription()
                {
                    Name = "Intex Decoder",
                    ShortName = "INTX",
                    Author = "Daniel Schenk",
                    VersionMajor = 0,
                    VersionMinor = 1,
                    Description = "Intex Saltwater System UI board protocol",
                    InputWaveformTypes = new Dictionary<string, Type>()
                    {
                        { "Input", typeof(bool)}
                    }
                };
            }
        }

        enum State { Idle, MeasuringBitStartPulse, MeasuringBitValuePulse, Disconnected }

        const double pulseThreshold = 150e-6;
        const double longBitPulseThreshold = 600e-6;
        const double timeoutThreshold = 900e-6;

        public DecoderOutput[] Process(Dictionary<string, Array> inputWaveforms, Dictionary<string, object> parameters, double samplePeriod)
        {
            bool[] input = (bool[])inputWaveforms["Input"];

            List<DecoderOutput> decoderOutputList = new List<DecoderOutput>();

            State state = input[0] ? State.Idle : State.Disconnected;
            int startIndex = 0;
            int bitCount = 0;
            int byteStartIndex = 0;
            int byteValue = 0;
            int timeoutIndex = 0;

            for (int i = 1; i < input.Length; i++)
            {
                bool risingEdge = input[i] && !input[i - 1];
                bool fallingEdge = !input[i] && input[i - 1];

                switch (state)
                {
                    case State.Idle:
                        {
                            if (fallingEdge)
                            {
                                state = State.MeasuringBitStartPulse;
                                startIndex = i;
                                if (bitCount == 0)
                                {
                                    byteStartIndex = i;
                                }
                                timeoutIndex = i + (int)Math.Ceiling(timeoutThreshold / samplePeriod);
                            }
                        }
                        break;
                    case State.Disconnected:
                        {
                            if (risingEdge)
                            {
                                state = State.Idle;
                            }
                        }
                        break;
                    case State.MeasuringBitStartPulse:
                        {
                            if (risingEdge)
                            {
                                double duration = (i - startIndex) * samplePeriod;
                                if (duration >= pulseThreshold)
                                {
                                    state = State.MeasuringBitValuePulse;
                                    startIndex = i;
                                    timeoutIndex = i + (int)Math.Ceiling(timeoutThreshold / samplePeriod);
                                }
                                else
                                {
                                    // glitch
                                }
                            }
                            else if (i >= timeoutIndex)
                            {
                                state = State.Disconnected;
                                decoderOutputList.Add(new DecoderOutputEvent(
                                    startIndex,
                                    i,
                                    DecoderOutputColor.Red,
                                    "ERROR"
                                ));
                            }
                        }
                        break;
                    case State.MeasuringBitValuePulse:
                        {
                            bool timedOut = i >= timeoutIndex;
                            if (fallingEdge || timedOut)
                            {
                                double duration = (i - startIndex) * samplePeriod;
                                if (duration >= pulseThreshold)
                                {
                                    if (duration >= longBitPulseThreshold)
                                    {
                                        byteValue |= (1 << bitCount);
                                    }
                                    if (++bitCount == 8)
                                    {
                                        decoderOutputList.Add(new DecoderOutputValueNumeric(
                                            byteStartIndex,
                                            i,
                                            DecoderOutputColor.Blue,
                                            byteValue,
                                            string.Empty,
                                            8
                                        ));
                                        bitCount = 0;
                                        byteValue = 0;
                                    }

                                    if (fallingEdge)
                                    {
                                        state = State.MeasuringBitStartPulse;
                                        startIndex = i;
                                        timeoutIndex = i + (int)Math.Ceiling(timeoutThreshold / samplePeriod);
                                        if (bitCount == 0)
                                        {
                                            byteStartIndex = i;
                                        }
                                    }
                                    else if (timedOut)
                                    {
                                        state = State.Idle;
                                        // the only valid moment for this timeout is at bitcount 1,
                                        // since then the start pulse wasn't to indicate a new bit of the next byte,
                                        // but the trailing pulse which ends a frame
                                        if (bitCount != 1)
                                        {
                                            bitCount = 0;
                                            byteValue = 0;
                                            decoderOutputList.Add(new DecoderOutputEvent(
                                                byteStartIndex,
                                                i,
                                                DecoderOutputColor.Red,
                                                "ERROR"
                                            ));
                                        }
                                    }
                                }
                                else
                                {
                                    // glitch
                                }
                            }
                        }
                        break;
                }
            }

            return decoderOutputList.ToArray();
        }
    }
}
