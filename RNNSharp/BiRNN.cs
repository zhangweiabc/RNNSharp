﻿using System;
using System.IO;
using System.Threading.Tasks;
using AdvUtils;

/// <summary>
/// RNNSharp written by Zhongkai Fu (fuzhongkai@gmail.com)
/// </summary>
namespace RNNSharp
{
    class BiRNN : RNN
    {
        private RNN forwardRNN;
        private RNN backwardRNN;

        public BiRNN(RNN s_forwardRNN, RNN s_backwardRNN)
        {
            forwardRNN = s_forwardRNN;
            backwardRNN = s_backwardRNN;

            ModelType = forwardRNN.ModelType;
            ModelDirection = MODELDIRECTION.BI_DIRECTIONAL;
        }

        public override int L0
        {
            get
            {
                return forwardRNN.L0;
            }

            set
            {
                forwardRNN.L0 = value;
                backwardRNN.L0 = value;
            }
        }

        public override int L2
        {
            get
            {
                return forwardRNN.L2;
            }

            set
            {
                forwardRNN.L2 = value;
                backwardRNN.L2 = value;
            }
        }

        public override void initWeights()
        {
            forwardRNN.initWeights();
            backwardRNN.initWeights();

        }

        public override string ModelFile
        {
            get { return forwardRNN.ModelFile; }
            set
            {
                forwardRNN.ModelFile = value;
                backwardRNN.ModelFile = value;
            }
        }

        public override long SaveStep
        {
            get
            {
                return forwardRNN.SaveStep;
            }

            set
            {
                forwardRNN.SaveStep = value;
                backwardRNN.SaveStep = value;
            }
        }

        public override int MaxIter
        {
            get
            {
                return forwardRNN.MaxIter;
            }

            set
            {
                forwardRNN.MaxIter = value;
                backwardRNN.MaxIter = value;
            }
        }

        public override bool IsCRFTraining
        {
            get { return forwardRNN.IsCRFTraining; }

            set
            {
                forwardRNN.IsCRFTraining = value;
                backwardRNN.IsCRFTraining = value;
            }
        }

        public override float LearningRate
        {
            get
            {
                return forwardRNN.LearningRate;
            }

            set
            {
                forwardRNN.LearningRate = value;
                backwardRNN.LearningRate = value;
            }
        }

        public override float GradientCutoff
        {
            get
            {
                return forwardRNN.GradientCutoff;
            }

            set
            {
                forwardRNN.GradientCutoff = value;
                backwardRNN.GradientCutoff = value;
            }
        }

        public override float Dropout
        {
            get
            {
                return forwardRNN.Dropout;
            }

            set
            {
                forwardRNN.Dropout = value;
                backwardRNN.Dropout = value;
            }
        }

        public override int L1
        {
            get
            {
                return forwardRNN.L1;
            }

            set
            {
                forwardRNN.L1 = value;
                backwardRNN.L1 = value;
            }
        }

        public override int DenseFeatureSize
        {
            get
            {
                return forwardRNN.DenseFeatureSize;
            }

            set
            {
                forwardRNN.DenseFeatureSize = value;
                backwardRNN.DenseFeatureSize = value;
            }
        }

        public override void GetHiddenLayer(Matrix<double> m, int curStatus)
        {
            throw new NotImplementedException("Not implement GetHiddenLayer");
        }

        public override void initMem()
        {
            forwardRNN.initMem();
            backwardRNN.initMem();

            //Create and intialise the weights from hidden to output layer, these are just normal weights
            Hidden2OutputWeight = new Matrix<double>(L2, L1);

            for (int i = 0; i < Hidden2OutputWeight.GetHeight(); i++)
            {
                for (int j = 0; j < Hidden2OutputWeight.GetWidth(); j++)
                {
                    Hidden2OutputWeight[i][j] = RandInitWeight();
                }
            }
        }

        public neuron[][] InnerDecode(Sequence pSequence, out Matrix<neuron> outputHiddenLayer, out Matrix<double> rawOutputLayer)
        {
            int numStates = pSequence.States.Length;
            Matrix<double> mForward = null;
            Matrix<double> mBackward = null;

            //Reset the network
            netReset(false);

            Parallel.Invoke(() =>
            {
                //Computing forward RNN
                mForward = new Matrix<double>(numStates, forwardRNN.L1);
                for (int curState = 0; curState < numStates; curState++)
                {
                    State state = pSequence.States[curState];
                    forwardRNN.setInputLayer(state, curState, numStates, null);
                    forwardRNN.computeNet(state, null);      //compute probability distribution

                    forwardRNN.GetHiddenLayer(mForward, curState);
                }
            },
             () =>
             {
                 //Computing backward RNN
                 mBackward = new Matrix<double>(numStates, backwardRNN.L1);
                 for (int curState = numStates - 1; curState >= 0; curState--)
                 {
                     State state = pSequence.States[curState];
                     backwardRNN.setInputLayer(state, curState, numStates, null, false);
                     backwardRNN.computeNet(state, null);      //compute probability distribution

                     backwardRNN.GetHiddenLayer(mBackward, curState);
                 }
             });

            //Merge forward and backward
            Matrix<neuron> mergedHiddenLayer = new Matrix<neuron>(numStates, forwardRNN.L1);
            Parallel.For(0, numStates, parallelOption, curState =>
            {
                for (int i = 0; i < forwardRNN.L1; i++)
                {
                    mergedHiddenLayer[curState][i].cellOutput = mForward[curState][i] + mBackward[curState][i];
                }
            });

            //Calculate output layer
            Matrix<double> tmp_rawOutputLayer = new Matrix<double>(numStates, L2);
            neuron[][] seqOutput = new neuron[numStates][];
            Parallel.For(0, numStates, parallelOption, curState =>
            {
                seqOutput[curState] = new neuron[L2];
                matrixXvectorADD(seqOutput[curState], mergedHiddenLayer[curState], Hidden2OutputWeight, 0, L2, 0, L1, 0);

                for (int i = 0; i < L2; i++)
                {
                    tmp_rawOutputLayer[curState][i] = seqOutput[curState][i].cellOutput;
                }

                //Activation on output layer
                SoftmaxLayer(seqOutput[curState]);
            });

            outputHiddenLayer = mergedHiddenLayer;
            rawOutputLayer = tmp_rawOutputLayer;

            return seqOutput;
        }

        public override int[] PredictSentenceCRF(Sequence pSequence, RunningMode runningMode)
        {
            //Reset the network
            int numStates = pSequence.States.Length;
            //Predict output
            Matrix<neuron> mergedHiddenLayer = null;
            Matrix<double> rawOutputLayer = null;
            neuron[][] seqOutput = InnerDecode(pSequence, out mergedHiddenLayer, out rawOutputLayer);

            ForwardBackward(numStates, rawOutputLayer);

            if (runningMode != RunningMode.Test)
            {
                //Get the best result
                for (int i = 0; i < numStates; i++)
                {
                    logp += Math.Log10(CRFSeqOutput[i][pSequence.States[i].Label]);
                }
            }

            int[] predict = Viterbi(rawOutputLayer, numStates);

            if (runningMode == RunningMode.Train)
            {
                UpdateBigramTransition(pSequence);

                //Update hidden-output layer weights
                for (int curState = 0; curState < numStates; curState++)
                {
                    int label = pSequence.States[curState].Label;
                    //For standard RNN
                    for (int c = 0; c < L2; c++)
                    {
                        seqOutput[curState][c].er = -CRFSeqOutput[curState][c];
                    }
                    seqOutput[curState][label].er = 1 - CRFSeqOutput[curState][label];
                }

                LearnTwoRNN(pSequence, mergedHiddenLayer, seqOutput);
            }

            return predict;
        }

        public override Matrix<double> PredictSentence(Sequence pSequence, RunningMode runningMode)
        {
            //Reset the network
            int numStates = pSequence.States.Length;

            //Predict output
            Matrix<neuron> mergedHiddenLayer = null;
            Matrix<double> rawOutputLayer = null;
            neuron[][] seqOutput = InnerDecode(pSequence, out mergedHiddenLayer, out rawOutputLayer);

            if (runningMode != RunningMode.Test)
            {
                //Merge forward and backward
                for (int curState = 0; curState < numStates; curState++)
                {
                    logp += Math.Log10(seqOutput[curState][pSequence.States[curState].Label].cellOutput);
                }
            }

            if (runningMode == RunningMode.Train)
            {
                //Update hidden-output layer weights
                for (int curState = 0; curState < numStates; curState++)
                {
                    int label = pSequence.States[curState].Label;
                    //For standard RNN
                    for (int c = 0; c < L2; c++)
                    {
                        seqOutput[curState][c].er = -seqOutput[curState][c].cellOutput;
                    }
                    seqOutput[curState][label].er = 1 - seqOutput[curState][label].cellOutput;
                }

                LearnTwoRNN(pSequence, mergedHiddenLayer, seqOutput);
            }

            return rawOutputLayer;
        }

        private void LearnTwoRNN(Sequence pSequence, Matrix<neuron> mergedHiddenLayer, neuron[][] seqOutput)
        {
            netReset(true);

            int numStates = pSequence.States.Length;
            forwardRNN.Hidden2OutputWeight = Hidden2OutputWeight.CopyTo();
            backwardRNN.Hidden2OutputWeight = Hidden2OutputWeight.CopyTo();

            Parallel.Invoke(() =>
                {
                    for (int curState = 0; curState < numStates; curState++)
                    {
                        for (int i = 0; i < Hidden2OutputWeight.GetHeight(); i++)
                        {
                            //update weights for hidden to output layer

                            for (int k = 0; k < Hidden2OutputWeight.GetWidth(); k++)
                            {
                                Hidden2OutputWeight[i][k] += LearningRate * mergedHiddenLayer[curState][k].cellOutput * seqOutput[curState][i].er;
                            }
                        }
                    }

                },
                ()=>
            {

                //Learn forward network
                for (int curState = 0; curState < numStates; curState++)
                {
                    // error propogation
                    State state = pSequence.States[curState];
                    forwardRNN.setInputLayer(state, curState, numStates, null);
                    forwardRNN.computeNet(state, null);      //compute probability distribution

                    //Copy output result to forward net work's output
                    forwardRNN.OutputLayer = seqOutput[curState];

                    forwardRNN.learnNet(state, curState, true);
                    forwardRNN.LearnBackTime(state, numStates, curState);
                }
            },
            () =>
            {

                for (int curState = 0; curState < numStates; curState++)
                {
                    int curState2 = numStates - 1 - curState;

                    // error propogation
                    State state2 = pSequence.States[curState2];
                    backwardRNN.setInputLayer(state2, curState2, numStates, null, false);
                    backwardRNN.computeNet(state2, null);      //compute probability distribution

                    //Copy output result to forward net work's output
                    backwardRNN.OutputLayer = seqOutput[curState2];

                    backwardRNN.learnNet(state2, curState2, true);
                    backwardRNN.LearnBackTime(state2, numStates, curState2);
                }
            });
        }


        public override void LearnBackTime(State state, int numStates, int curState)
        {
        }

        public override void learnNet(State state, int timeat, bool biRNN = false)
        {

        }

        public override void computeNet(State state, double[] doutput, bool isTrain = true)
        {

        }

        public override void netReset(bool updateNet = false)
        {
            forwardRNN.netReset(updateNet);
            backwardRNN.netReset(updateNet);
        }

        public override void saveNetBin(string filename)
        {
            //Save bi-directional model
            forwardRNN.Hidden2OutputWeight = Hidden2OutputWeight;
            backwardRNN.Hidden2OutputWeight = Hidden2OutputWeight;

            forwardRNN.CRFTagTransWeights = CRFTagTransWeights;
            backwardRNN.CRFTagTransWeights = CRFTagTransWeights;

            forwardRNN.saveNetBin(filename + ".forward");
            backwardRNN.saveNetBin(filename + ".backward");

            //Save meta data
            using (StreamWriter sw = new StreamWriter(filename))
            {
                BinaryWriter fo = new BinaryWriter(sw.BaseStream);
                fo.Write((int)ModelType);
                fo.Write((int)ModelDirection);

                // Signiture , 0 is for RNN or 1 is for RNN-CRF
                int iflag = 0;
                if (IsCRFTraining == true)
                {
                    iflag = 1;
                }
                fo.Write(iflag);

                fo.Write(L0);
                fo.Write(L1);
                fo.Write(L2);
                fo.Write(DenseFeatureSize);
            }
        }

        public override void loadNetBin(string filename)
        {
            Logger.WriteLine(Logger.Level.info, "Loading bi-directional model: {0}", filename);

            forwardRNN.loadNetBin(filename + ".forward");
            backwardRNN.loadNetBin(filename + ".backward");

            Hidden2OutputWeight = forwardRNN.Hidden2OutputWeight;
            CRFTagTransWeights = forwardRNN.CRFTagTransWeights;

            using (StreamReader sr = new StreamReader(filename))
            {
                BinaryReader br = new BinaryReader(sr.BaseStream);

                ModelType = (MODELTYPE)br.ReadInt32();
                ModelDirection = (MODELDIRECTION)br.ReadInt32();

                int iflag = br.ReadInt32();
                if (iflag == 1)
                {
                    IsCRFTraining = true;
                }
                else
                {
                    IsCRFTraining = false;
                }

                //Load basic parameters
                L0 = br.ReadInt32();
                L1 = br.ReadInt32();
                L2 = br.ReadInt32();
                DenseFeatureSize = br.ReadInt32();
            }
        }
    }
}
