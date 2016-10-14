﻿using System;
using System.Collections.Generic;
using System.Linq;
using KelpNet.Common;
using KelpNet.Interface;

namespace KelpNet.Functions.Connections
{
    [Serializable]
    public class LSTM : Function, IPredictableFunction
    {
        private Stack<double[]>[] aParam;
        private Stack<double[]>[] iParam;
        private Stack<double[]>[] fParam;
        private Stack<double[]>[] oParam;
        private Stack<double[]>[] cParam = new Stack<double[]>[1];

        private NdArray[] hParam = new NdArray[1];

        public Linear[] upward = new Linear[4];
        public Linear[] lateral = new Linear[4];

        private NdArray[][] gxPrev = new NdArray[1][];
        private double[][] gcPrev = new double[1][];

        public LSTM(int inSize, int outSize, Array initialUpwardW = null, Array initialUpwardb = null, Array initialLateralW = null, string name = "LSTM") : base(name)
        {
            for (int i = 0; i < 4; i++)
            {
                this.upward[i] = new Linear(inSize, outSize, noBias: false, initialW: initialUpwardW, initialb: initialUpwardb, name: "upward" + i);
                Parameters.Add(new OptimizeParameter(this.upward[i].W, this.upward[i].gW, this.Name + " " + this.upward[i].Name + " W"));
                Parameters.Add(new OptimizeParameter(this.upward[i].b, this.upward[i].gb, this.Name + " " + this.upward[i].Name + " b"));

                //lateralはBiasは無し
                this.lateral[i] = new Linear(outSize, outSize, noBias: true, initialW: initialLateralW, name: "lateral" + i);
                Parameters.Add(new OptimizeParameter(this.lateral[i].W, this.lateral[i].gW, this.Name + " " + this.lateral[i].Name + " W"));
            }

            InputCount = inSize;
            OutputCount = outSize;
        }

        protected override NdArray ForwardSingle(NdArray x, int batchID = 0)
        {
            if (this.cParam[batchID].Count == 0)
            {
                this.cParam[batchID].Push(new double[OutputCount]);
            }

            double[] upwardResult = new double[OutputCount * 4];
            Array.Copy(this.upward[0].Forward(x, batchID).Data, 0, upwardResult, 0 * OutputCount, OutputCount);
            Array.Copy(this.upward[1].Forward(x, batchID).Data, 0, upwardResult, 1 * OutputCount, OutputCount);
            Array.Copy(this.upward[2].Forward(x, batchID).Data, 0, upwardResult, 2 * OutputCount, OutputCount);
            Array.Copy(this.upward[3].Forward(x, batchID).Data, 0, upwardResult, 3 * OutputCount, OutputCount);

            NdArray[] r;
            if (this.hParam[batchID] != null)
            {
                double[] lateralResult = new double[OutputCount * 4];
                Array.Copy(this.lateral[0].Forward(this.hParam[batchID], batchID).Data, 0, lateralResult, 0 * OutputCount, OutputCount);
                Array.Copy(this.lateral[1].Forward(this.hParam[batchID], batchID).Data, 0, lateralResult, 1 * OutputCount, OutputCount);
                Array.Copy(this.lateral[2].Forward(this.hParam[batchID], batchID).Data, 0, lateralResult, 2 * OutputCount, OutputCount);
                Array.Copy(this.lateral[3].Forward(this.hParam[batchID], batchID).Data, 0, lateralResult, 3 * OutputCount, OutputCount);

                //加算しつつ再配置
                r = this.ExtractGates(upwardResult, lateralResult);
            }
            else
            {
                this.hParam[batchID] = NdArray.Zeros(OutputCount);

                r = this.ExtractGates(upwardResult);
            }

            var la = new double[OutputCount];
            var li = new double[OutputCount];
            var lf = new double[OutputCount];
            var lo = new double[OutputCount];
            var cPrev = this.cParam[batchID].Peek();
            var cResult = new double[cPrev.Length];

            for (int i = 0; i < this.hParam[batchID].Length; i++)
            {
                la[i] = Math.Tanh(r[0].Data[i]);
                li[i] = Sigmoid(r[1].Data[i]);
                lf[i] = Sigmoid(r[2].Data[i]);
                lo[i] = Sigmoid(r[3].Data[i]);

                cResult[i] = la[i] * li[i] + lf[i] * cPrev[i];
                this.hParam[batchID].Data[i] = lo[i] * Math.Tanh(cResult[i]);
            }

            //Backward用
            this.cParam[batchID].Push(cResult);
            this.aParam[batchID].Push(la);
            this.iParam[batchID].Push(li);
            this.fParam[batchID].Push(lf);
            this.oParam[batchID].Push(lo);

            return this.hParam[batchID];
        }

        public override void ResetState()
        {
            this.cParam = new Stack<double[]>[this.cParam.Length];
            for (int i = 0; i < this.cParam.Length; i++)
            {
                this.cParam[i] = new Stack<double[]>();
            }

            this.hParam = new NdArray[this.hParam.Length];

            this.gcPrev = new double[this.gcPrev.Length][];
            this.gxPrev = new NdArray[this.gxPrev.Length][];
        }

        protected override NdArray BackwardSingle(NdArray gh, int batchID = 0)
        {
            if (this.gxPrev[batchID] != null)
            {
                for (int i = 0; i < 4; i++)
                {
                    var ghPre = this.lateral[i].Backward(this.gxPrev[batchID][i], batchID);

                    for (int j = 0; j < ghPre.Length; j++)
                    {
                        gh.Data[j] += ghPre.Data[j];
                    }
                }
            }

            if (this.gcPrev[batchID] == null)
            {
                this.gcPrev[batchID] = new double[InputCount];
            }

            var ga = new double[InputCount];
            var gi = new double[InputCount];
            var gf = new double[InputCount];
            var go = new double[InputCount];

            var lcParam = this.cParam[batchID].Pop();
            var cPrev = this.cParam[batchID].Peek();
            var laParam = this.aParam[batchID].Pop();
            var liParam = this.iParam[batchID].Pop();
            var lfParam = this.fParam[batchID].Pop();
            var loParam = this.oParam[batchID].Pop();

            for (int i = 0; i < this.gcPrev[batchID].Length; i++)
            {
                var co = Math.Tanh(lcParam[i]);

                this.gcPrev[batchID][i] = gh.Data[i] * loParam[i] * GradTanh(co) + this.gcPrev[batchID][i];
                ga[i] = this.gcPrev[batchID][i] * liParam[i] * GradTanh(laParam[i]);
                gi[i] = this.gcPrev[batchID][i] * laParam[i] * GradSigmoid(liParam[i]);
                gf[i] = this.gcPrev[batchID][i] * cPrev[i] * GradSigmoid(lfParam[i]);
                go[i] = gh.Data[i] * co * GradSigmoid(loParam[i]);

                this.gcPrev[batchID][i] *= lfParam[i];
            }

            var r = this.RestoreGates(ga, gi, gf, go);
            this.gxPrev[batchID] = r;

            ga = this.upward[0].Backward(r[0], batchID).Data;
            gi = this.upward[1].Backward(r[1], batchID).Data;
            gf = this.upward[2].Backward(r[2], batchID).Data;
            go = this.upward[3].Backward(r[3], batchID).Data;

            double[] gx = new double[InputCount];
            for (int i = 0; i < ga.Length; i++)
            {
                gx[i] = ga[i] + gi[i] + gf[i] + go[i];
            }

            return NdArray.FromArray(gx);
        }

        //バッチ実行時にバッティングするメンバをバッチ数分用意
        public override void InitBatch(int batchCount)
        {
            for (int i = 0; i < 4; i++)
            {
                this.upward[i].InitBatch(batchCount);
                this.lateral[i].InitBatch(batchCount);
            }

            this.hParam = new NdArray[batchCount];
            this.aParam = new Stack<double[]>[batchCount];
            this.iParam = new Stack<double[]>[batchCount];
            this.fParam = new Stack<double[]>[batchCount];
            this.oParam = new Stack<double[]>[batchCount];
            this.cParam = new Stack<double[]>[batchCount];

            for (int i = 0; i < batchCount; i++)
            {
                this.aParam[i] = new Stack<double[]>();
                this.iParam[i] = new Stack<double[]>();
                this.fParam[i] = new Stack<double[]>();
                this.oParam[i] = new Stack<double[]>();
                this.cParam[i] = new Stack<double[]>();
            }

            this.gcPrev = new double[batchCount][];
            this.gxPrev = new NdArray[batchCount][];
        }

        public NdArray Predict(NdArray input, int batchID)
        {
            return this.ForwardSingle(input, batchID);
        }

        static double Sigmoid(double x)
        {
            return 1 / (1 + Math.Exp(-x));
        }

        static double GradSigmoid(double x)
        {
            return x * (1 - x);
        }

        static double GradTanh(double x)
        {
            return 1 - x * x;
        }

        NdArray[] RestoreGates(params double[][] x)
        {
            int col = x[0].Length;
            double[] r = new double[4 * col];

            for (int i = 0; i < col; i++)
            {
                r[i * 4 + 0] = x[0][i];
                r[i * 4 + 1] = x[1][i];
                r[i * 4 + 2] = x[2][i];
                r[i * 4 + 3] = x[3][i];
            }

            NdArray[] result = new NdArray[4];

            for (int i = 0; i < result.Length; i++)
            {
                double[] data = new double[col];
                for (int j = 0; j < col; j++)
                {
                    data[j] = r[i * col + j];
                }
                result[i] = new NdArray(data, new[] { data.Length });
            }

            return result;
        }

        NdArray[] ExtractGates(params double[][] x)
        {
            int col = x[0].Length / 4;

            double[] data1 = new double[col];
            double[] data2 = new double[col];
            double[] data3 = new double[col];
            double[] data4 = new double[col];

            for (int i = 0; i < x.Length; i++)
            {
                for (int j = 0; j < col; j++)
                {
                    data1[j] += x[i][j * 4];
                    data2[j] += x[i][j * 4 + 1];
                    data3[j] += x[i][j * 4 + 2];
                    data4[j] += x[i][j * 4 + 3];
                }
            }

            int[] shape = { col };
            NdArray[] r =
            {
                new NdArray(data1,shape),
                new NdArray(data2,shape),
                new NdArray(data3,shape),
                new NdArray(data4,shape)
            };

            return r;
        }
    }
}
