﻿using System;
using System.Collections.Generic;
using KelpNet.Common.Optimizers;

namespace KelpNet.Common.Functions
{
    //層を積み上げるこのライブラリのメインとなるクラス
    //一回のForward、Backward、Updateで同時に実行される関数の集まり
    [Serializable]
    public class FunctionStack : Function
    {
        const string FUNCTION_NAME = "FunctionStack";

        //すべての層がココにFunctionクラスとして保管される
        public Function[] Functions { get; private set; }

        //コンストラクタ
        public FunctionStack(params Function[] functions) : base(FUNCTION_NAME)
        {
            this.Functions = functions;
        }

        public FunctionStack(params FunctionStack[] functionStacks) : base(FUNCTION_NAME)
        {
            List<Function> functionList = new List<Function>();

            foreach (FunctionStack functionStack in functionStacks)
            {
                foreach (Function function in functionStack.Functions)
                {
                    functionList.Add(function);
                }
            }

            this.Functions = functionList.ToArray();
        }

        public void Compress()
        {
            List<Function> functionList = new List<Function>(Functions);

            //層を圧縮
            for (int i = 0; i < functionList.Count - 1; i++)
            {
                if (functionList[i] is CompressibleFunction)
                {
                    if (functionList[i + 1] is CompressibleActivation)
                    {
                        ((CompressibleFunction)functionList[i]).SetActivation((CompressibleActivation)functionList[i + 1]);
                        functionList.RemoveAt(i + 1);
                    }
                }
            }

            this.Functions = functionList.ToArray();
        }

        //Forward
        public override NdArray Forward(params NdArray[] xs)
        {
            NdArray result = this.Functions[0].Forward(xs);

            for (int i = 1; i < this.Functions.Length; i++)
            {
                result = this.Functions[i].Forward(result);
            }

            return result;
        }

        public override void Backward(NdArray y)
        {
            if (y.UseCount == 0 && y.ParentFunc != null)
            {
                y.ParentFunc.Backward(y);

                List<NdArray[]> prevInputs = y.ParentFunc.PrevInputs;
                NdArray[] xs = prevInputs[prevInputs.Count - 1];

                for (int i = 0; i < xs.Length; i++)
                {
                    Backward(xs[i]);
                }
            }
        }

        //重みの更新処理
        public override void Update()
        {
            foreach (var function in Functions)
            {
                //更新実行前に訓練カウントを使って各Functionの傾きを補正
                function.Update();
            }
        }

        //ある処理実行後に特定のデータを初期値に戻す処理
        public override void ResetState()
        {
            foreach (Function function in this.Functions)
            {
                function.ResetState();
            }
        }

        //予想を実行する
        public override NdArray Predict(params NdArray[] xs)
        {
            NdArray y = this.Functions[0].Predict(xs);

            for (int i = 1; i < this.Functions.Length; i++)
            {
                y = this.Functions[i].Predict(y);
            }

            return y;
        }

        public override void SetOptimizer(params Optimizer[] optimizers)
        {
            foreach (Function function in this.Functions)
            {
                function.SetOptimizer(optimizers);
            }
        }
    }
}