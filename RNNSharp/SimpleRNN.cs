﻿using System;
using System.Threading.Tasks;
using System.IO;
using AdvUtils;

/// <summary>
/// RNNSharp written by Zhongkai Fu (fuzhongkai@gmail.com)
/// </summary>
namespace RNNSharp
{
    public class SimpleRNN : RNN
    {
        protected int bptt;
        protected int bptt_block;
        protected neuron[] bptt_hidden;
        protected double[] bptt_fea;
        protected SparseVector[] bptt_inputs = new SparseVector[MAX_RNN_HIST];    // TODO: add const constraint

        protected Matrix<double> mat_bptt_syn0_w;
        protected Matrix<double> mat_bptt_syn0_ph;

        protected Matrix<double> mat_bptt_synf;
        protected Matrix<double> mat_hiddenBpttWeight;

        protected neuron[] neuLastHidden;		//neurons in input layer
        protected neuron[] neuHidden;		//neurons in hidden layer
        protected Matrix<double> mat_input2hidden;
        protected Matrix<double> mat_feature2hidden;

        public SimpleRNN()
        {
            ModelType = MODELTYPE.SIMPLE;
            GradientCutoff = 15;
            Dropout = 0;

            L1 = 30;
            bptt = 5;
            bptt_block = 10;
            bptt_hidden = null;
            bptt_fea = null;


            DenseFeatureSize = 0;

            neuLastHidden = null;
            neuFeatures = null;
            neuHidden = null;
            OutputLayer = null;
        }

        public void setBPTT(int newval) { bptt = newval; }
        public void setBPTTBlock(int newval) { bptt_block = newval; }


        public override void initWeights()
        {
            int b, a;
            for (b = 0; b < L1; b++)
            {
                for (a = 0; a < L0; a++)
                {
                    mat_input2hidden[b][a] = RandInitWeight();
                }
            }


            for (b = 0; b < L1; b++)
            {
                for (a = 0; a < DenseFeatureSize; a++)
                {
                    mat_feature2hidden[b][a] = RandInitWeight();

                }
            }

            for (b = 0; b < Hidden2OutputWeight.GetHeight(); b++)
            {
                for (a = 0; a < L1; a++)
                {
                    Hidden2OutputWeight[b][a] = RandInitWeight();
                }
            }

            for (b = 0; b < L1; b++)
            {
                for (a = 0; a < L1; a++)
                {
                    mat_hiddenBpttWeight[b][a] = RandInitWeight();
                }
            }
        }

        public override void GetHiddenLayer(Matrix<double> m, int curStatus)
        {
            for (int i = 0; i < L1; i++)
            {
                m[curStatus][i] = neuHidden[i].cellOutput;
            }
        }

        public void computeHiddenActivity(bool isTrain)
        {
            Parallel.For(0, L1, parallelOption, a =>
            {
                if (neuHidden[a].mask == true)
                {
                    neuHidden[a].cellOutput = 0;
                    return;
                }

                if (isTrain == false)
                {
                    neuHidden[a].cellOutput = neuHidden[a].cellOutput * (1.0 - Dropout);
                }

                if (neuHidden[a].cellOutput > 50) neuHidden[a].cellOutput = 50;  //for numerical stability
                if (neuHidden[a].cellOutput < -50) neuHidden[a].cellOutput = -50;  //for numerical stability
                neuHidden[a].cellOutput = 1.0 / (1.0 + Math.Exp(-neuHidden[a].cellOutput));
            });
        }

        // forward process. output layer consists of tag value
        public override void computeNet(State state, double[] doutput, bool isTrain = true)
        {
            //keep last hidden layer and erase activations
            neuLastHidden = neuHidden;

            //hidden(t-1) -> hidden(t)
            neuHidden = new neuron[L1];
            matrixXvectorADD(neuHidden, neuLastHidden, mat_hiddenBpttWeight, 0, L1, 0, L1, 0);

            //Apply feature values on hidden layer
            var sparse = state.SparseData;
            int n = sparse.GetNumberOfEntries();
            Parallel.For(0, L1, parallelOption, b =>
            {
                //Sparse features:
                //inputs(t) -> hidden(t)
                //Get sparse feature and apply it into hidden layer
                for (int i = 0; i < n; i++)
                {
                    var entry = sparse.GetEntry(i);
                    neuHidden[b].cellOutput += entry.Value * mat_input2hidden[b][entry.Key];
                }

                //Dense features:
                //fea(t) -> hidden(t) 
                if (DenseFeatureSize > 0)
                {
                    for (int j = 0; j < DenseFeatureSize; j++)
                    {
                        neuHidden[b].cellOutput += neuFeatures[j] * mat_feature2hidden[b][j];
                    }
                }
            });

            //activate 1      --sigmoid
            computeHiddenActivity(isTrain);

            //Calculate output layer
            matrixXvectorADD(OutputLayer, neuHidden, Hidden2OutputWeight, 0, L2, 0, L1, 0);
            if (doutput != null)
            {
                for (int i = 0; i < L2; i++)
                {
                    doutput[i] = OutputLayer[i].cellOutput;
                }
            }

            //activation 2   --softmax on words
            SoftmaxLayer(OutputLayer);
        }

        public override void learnNet(State state, int timeat, bool biRNN = false)
        {
            if (biRNN == false)
            {
                CalculateOutputLayerError(state, timeat);
            }

            //error output->hidden for words from specific class    	
            matrixXvectorADD(neuHidden, OutputLayer, Hidden2OutputWeight, 0, L1, 0, L2, 1);

            //Apply drop out on error in hidden layer
            for (int i = 0; i < L1; i++)
            {
                if (neuHidden[i].mask == true)
                {
                    neuHidden[i].er = 0;
                }
            }

            //Update hidden-output weights
            Parallel.For(0, L1, parallelOption, a =>
            {
                for (int c = 0; c < L2; c++)
                {
                    Hidden2OutputWeight[c][a] += LearningRate * OutputLayer[c].er * neuHidden[a].cellOutput;
                }
            });
        }

        void learnBptt(State state)
        {
            for (int step = 0; step < bptt + bptt_block - 2; step++)
            {
                if (null == bptt_inputs[step])
                    break;

                var sparse = bptt_inputs[step];
                Parallel.For(0, L1, parallelOption, a =>
                {
                    // compute hidden layer gradient
                    neuHidden[a].er *= neuHidden[a].cellOutput * (1 - neuHidden[a].cellOutput);

                    //dense weight update fea->0
                    if (DenseFeatureSize > 0)
                    {
                        for (int i = 0; i < DenseFeatureSize; i++)
                        {
                            mat_bptt_synf[a][i] += neuHidden[a].er * bptt_fea[i + step * DenseFeatureSize];
                        }
                    }

                    //sparse weight update hidden->input
                    for (int i = 0; i < sparse.GetNumberOfEntries(); i++)
                    {
                        mat_bptt_syn0_w[a][sparse.GetEntry(i).Key] += neuHidden[a].er * sparse.GetEntry(i).Value;
                    }

                    //bptt weight update
                    for (int i = 0; i < L1; i++)
                    {
                        mat_bptt_syn0_ph[a][i] += neuHidden[a].er * neuLastHidden[i].cellOutput;
                    }

                });

                //propagates errors hidden->input to the recurrent part
                matrixXvectorADD(neuLastHidden, neuHidden, mat_hiddenBpttWeight, 0, L1, 0, L1, 1);

                for (int a = 0; a < L1; a++)
                {
                    //propagate error from time T-n to T-n-1
                    neuHidden[a].er = neuLastHidden[a].er + bptt_hidden[(step + 1) * L1 + a].er;
                }

                if (step < bptt + bptt_block - 3)
                {
                    for (int a = 0; a < L1; a++)
                    {
                        neuHidden[a].cellOutput = bptt_hidden[(step + 1) * L1 + a].cellOutput;
                        neuLastHidden[a].cellOutput = bptt_hidden[(step + 2) * L1 + a].cellOutput;
                    }
                }
            }

            for (int b = 0; b < L1; b++)
            {
                neuHidden[b].cellOutput = bptt_hidden[b].cellOutput;		//restore hidden layer after bptt
            }


            Parallel.For(0, L1, parallelOption, b =>
            {
                //Update bptt feature weights
                for (int i = 0; i < L1; i++)
                {
                    mat_hiddenBpttWeight[b][i] += LearningRate * mat_bptt_syn0_ph[b][i];
                    //Clean bptt weight error
                    mat_bptt_syn0_ph[b][i] = 0;
                }

                //Update dense feature weights
                if (DenseFeatureSize > 0)
                {
                    for (int i = 0; i < DenseFeatureSize; i++)
                    {
                        mat_feature2hidden[b][i] += LearningRate * mat_bptt_synf[b][i];
                        //Clean dense feature weights error
                        mat_bptt_synf[b][i] = 0;
                    }
                }

                //Update sparse feature weights
                for (int step = 0; step < bptt + bptt_block - 2; step++)
                {
                    if (null == bptt_inputs[step])
                        break;

                    var sparse = bptt_inputs[step];
                    for (int i = 0; i < sparse.GetNumberOfEntries(); i++)
                    {
                        int pos = sparse.GetEntry(i).Key;
                        mat_input2hidden[b][pos] += LearningRate * mat_bptt_syn0_w[b][pos];

                        //Clean sparse feature weight error
                        mat_bptt_syn0_w[b][pos] = 0;
                    }
                }
            });
        }


        public void resetBpttMem()
        {
            bptt_inputs = new SparseVector[MAX_RNN_HIST];
            bptt_hidden = new neuron[(bptt + bptt_block + 1) * L1];
            bptt_fea = new double[(bptt + bptt_block + 2) * DenseFeatureSize];
            mat_bptt_syn0_w = new Matrix<double>(L1, L0);
            mat_bptt_syn0_ph = new Matrix<double>(L1, L1);
            mat_bptt_synf = new Matrix<double>(L1, DenseFeatureSize);
        }

        public override void initMem()
        {
            CreateCells();

            Hidden2OutputWeight = new Matrix<double>(L2, L1);
            mat_input2hidden = new Matrix<double>(L1, L0);
            mat_feature2hidden = new Matrix<double>(L1, DenseFeatureSize);

            mat_hiddenBpttWeight = new Matrix<double>(L1, L1);


            Logger.WriteLine(Logger.Level.info, "[TRACE] Initializing weights, random value is {0}", rand.NextDouble());// yy debug
            initWeights();

            //Initialize BPTT
            resetBpttMem();
        }

        public override void netReset(bool updateNet = false)   //cleans hidden layer activation + bptt history
        {
            for (int a = 0; a < L1; a++)
            {
                neuHidden[a].cellOutput = 0.1;
                neuHidden[a].mask = false;
            }

            if (updateNet == true)
            {
                //Train mode
                for (int a = 0; a < L1; a++)
                {
                    if (rand.NextDouble() < Dropout)
                    {
                        neuHidden[a].mask = true;
                    }
                }

                Array.Clear(bptt_inputs, 0, MAX_RNN_HIST);
                Array.Clear(bptt_hidden, 0, (bptt + bptt_block + 1) * L1);
                Array.Clear(bptt_fea, 0, (bptt + bptt_block + 2) * DenseFeatureSize);
            }
        }


        public override void LearnBackTime(State state, int numStates, int curState)
        {
            int maxBptt = 0;
            for (maxBptt = 0; maxBptt < bptt + bptt_block - 1; maxBptt++)
            {
                if (bptt_inputs[maxBptt] == null)
                {
                    break;
                }
            }

            //shift memory needed for bptt to next time step
            for (int a = maxBptt; a > 0; a--)
            {
                bptt_inputs[a] = bptt_inputs[a - 1];
                Array.Copy(bptt_hidden, (a - 1) * L1, bptt_hidden, a * L1, L1);
                Array.Copy(bptt_fea, (a - 1) * DenseFeatureSize, bptt_fea, a * DenseFeatureSize, DenseFeatureSize);
            }
            bptt_inputs[0] = state.SparseData;

            //Save hidden and feature layer nodes values for bptt
            Array.Copy(neuHidden, 0, bptt_hidden, 0, L1);

            for (int i = 0; i < DenseFeatureSize; i++)
            {
                bptt_fea[i] = neuFeatures[i];
            }

            // time to learn bptt
            if (((curState % bptt_block) == 0) || (curState == numStates - 1))
            {
                learnBptt(state);
            }
        }

        public override void loadNetBin(string filename)
        {
            Logger.WriteLine(Logger.Level.info, "Loading SimpleRNN model: {0}", filename);

            StreamReader sr = new StreamReader(filename);
            BinaryReader br = new BinaryReader(sr.BaseStream);

            ModelType = (MODELTYPE)br.ReadInt32();
            if (ModelType != MODELTYPE.SIMPLE)
            {
                throw new Exception("Invalidated model format: must be simple RNN");
            }

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

            //Create cells of each layer
            CreateCells();

            //Load weight matrix between each two layer pairs
            Logger.WriteLine("Loading input2hidden weights...");
            mat_input2hidden = loadMatrixBin(br);

            Logger.WriteLine("Loading bptt hidden weights...");
            mat_hiddenBpttWeight = loadMatrixBin(br);

            Logger.WriteLine("Loading feature2hidden weights...");
            mat_feature2hidden = loadMatrixBin(br);

            Logger.WriteLine("Loading hidden2output weights...");
            Hidden2OutputWeight = loadMatrixBin(br);

            if (iflag == 1)
            {
                Logger.WriteLine("Loading CRF tag trans weights...");
                CRFTagTransWeights = loadMatrixBin(br);
            }

            sr.Close();
        }

        private void CreateCells()
        {
            neuFeatures = new SingleVector(DenseFeatureSize);
            OutputLayer = new neuron[L2];
            neuHidden = new neuron[L1];
        }

        // save model as binary format
        public override void saveNetBin(string filename) 
        {
            StreamWriter sw = new StreamWriter(filename);
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


            //weight input->hidden
            Logger.WriteLine("Saving input2hidden weights...");
            saveMatrixBin(mat_input2hidden, fo);

            Logger.WriteLine("Saving bptt hidden weights...");
            saveMatrixBin(mat_hiddenBpttWeight, fo);

            //weight fea->hidden
            Logger.WriteLine("Saving feature2hidden weights...");
            saveMatrixBin(mat_feature2hidden, fo);

            //weight hidden->output
            Logger.WriteLine("Saving hidden2output weights...");
            saveMatrixBin(Hidden2OutputWeight, fo);

            if (iflag == 1)
            {
                // Save Bigram
                saveMatrixBin(CRFTagTransWeights, fo);
            }

            fo.Close();
        }
    }
}
