﻿using System;
using System.Collections.Generic;
using System.IO;
using AdvUtils;

/// <summary>
/// RNNSharp written by Zhongkai Fu (fuzhongkai@gmail.com)
/// </summary>
namespace RNNSharp
{
    enum TFEATURE_WEIGHT_TYPE_ENUM
    {
        BINARY,
        FREQUENCY
    }

    public class Featurizer
    {
        public TagSet TagSet { get; set; }

        Dictionary<string, List<int>> m_FeatureConfiguration;
        int m_SparseDimension;
        int m_DenseDimension;
        int m_WordEmbeddingCloumn;
        TFEATURE_WEIGHT_TYPE_ENUM m_TFeatureWeightType = TFEATURE_WEIGHT_TYPE_ENUM.BINARY;
        WordEMWrapFeaturizer m_WordEmbedding;
        TemplateFeaturizer m_TFeaturizer;

        static string TFEATURE_CONTEXT = "TFEATURE_CONTEXT";
        static string WORDEMBEDDING_CONTEXT = "WORDEMBEDDING_CONTEXT";
        static string TFEATURE_FILENAME = "TFEATURE_FILENAME";
        static string WORDEMBEDDING_FILENAME = "WORDEMBEDDING_FILENAME";
        static string RT_FEATURE_CONTEXT = "RTFEATURE_CONTEXT";
        static string WORDEMBEDDING_COLUMN = "WORDEMBEDDING_COLUMN";
        static string TFEATURE_WEIGHT_TYPE = "TFEATURE_WEIGHT_TYPE";

        //The format of configuration file
        public void LoadFeatureConfigFromFile(string strFileName)
        {
            StreamReader sr = new StreamReader(strFileName);
            string strLine = null;

            m_FeatureConfiguration = new Dictionary<string, List<int>>();

            while ((strLine = sr.ReadLine()) != null)
            {
                strLine = strLine.Trim();
                if (strLine.Length == 0)
                {
                    //Emtpy line, ignore it
                    continue;
                }

                if (strLine.StartsWith("#") == true)
                {
                    //Comments line, ignore it
                    continue;
                }

                string[] kv = strLine.Split(':');
                string strKey = kv[0].Trim();
                string strValue = kv[1].Trim().ToLower();
                if (strKey == WORDEMBEDDING_FILENAME)
                {
                    Logger.WriteLine(Logger.Level.info, "Loading word embedding feature set...");
                    m_WordEmbedding = new WordEMWrapFeaturizer(strValue);
                    continue;
                }
                else if (strKey == TFEATURE_FILENAME)
                {
                    Logger.WriteLine(Logger.Level.info, "Loading template feature set...");
                    m_TFeaturizer = new TemplateFeaturizer(strValue);
                    continue;
                }
                else if (strKey == WORDEMBEDDING_COLUMN)
                {
                    m_WordEmbeddingCloumn = int.Parse(strValue);
                    Logger.WriteLine(Logger.Level.info, "Word embedding feature column: {0}", m_WordEmbeddingCloumn);
                    continue;
                }
                else if (strKey == TFEATURE_WEIGHT_TYPE)
                {
                    Logger.WriteLine(Logger.Level.info, "TFeature weighting type: {0}", strValue);
                    if (strValue == "binary")
                    {
                        m_TFeatureWeightType = TFEATURE_WEIGHT_TYPE_ENUM.BINARY;
                    }
                    else
                    {
                        m_TFeatureWeightType = TFEATURE_WEIGHT_TYPE_ENUM.FREQUENCY;
                    }

                    continue;
                }

                string[] values = strValue.Split(',');

                if (m_FeatureConfiguration.ContainsKey(strKey) == false)
                {
                    m_FeatureConfiguration.Add(strKey, new List<int>());
                }

                foreach (string value in values)
                {
                    m_FeatureConfiguration[strKey].Add(int.Parse(value));
                }
            }

            sr.Close();
        }

        // truncate current to range [lower, upper)
        public int TruncPosition(int current, int lower, int upper)
        {
            return (current < lower) ? lower : ((current >= upper) ? upper - 1 : current);
        }

        public Featurizer(string strFeatureConfigFileName, TagSet tagSet)
        {
            LoadFeatureConfigFromFile(strFeatureConfigFileName);
            TagSet = tagSet;
            InitComponentFeaturizer();
        }

        void InitComponentFeaturizer()
        {
            var fc = m_FeatureConfiguration;
            m_SparseDimension = 0;
            if (m_TFeaturizer != null)
            {
                if (fc.ContainsKey(TFEATURE_CONTEXT) == true)
                {
                    m_SparseDimension += m_TFeaturizer.GetFeatureSize() * fc[TFEATURE_CONTEXT].Count;
                }
            }

            if (fc.ContainsKey(RT_FEATURE_CONTEXT) == true)
            {
                m_SparseDimension += TagSet.GetSize() * fc[RT_FEATURE_CONTEXT].Count;
            }

            m_DenseDimension = 0;
            if (m_WordEmbedding != null)
            {
                if (fc.ContainsKey(WORDEMBEDDING_CONTEXT) == true)
                {
                    m_DenseDimension += m_WordEmbedding.GetDimension() * fc[WORDEMBEDDING_CONTEXT].Count;
                }
            }
        }

        public bool IsRunTimeFeatureUsed()
        {
            var fc = m_FeatureConfiguration;
            return fc.ContainsKey(RT_FEATURE_CONTEXT);
        }

        public void ShowFeatureSize()
        {
            var fc = m_FeatureConfiguration;

            if (m_TFeaturizer != null)
                Logger.WriteLine(Logger.Level.info, "Template feature size: {0}", m_TFeaturizer.GetFeatureSize());

            if (fc.ContainsKey(TFEATURE_CONTEXT) == true)
                Logger.WriteLine(Logger.Level.info, "Template feature context size: {0}", m_TFeaturizer.GetFeatureSize() * fc[TFEATURE_CONTEXT].Count);

            if (fc.ContainsKey(RT_FEATURE_CONTEXT) == true)
                Logger.WriteLine(Logger.Level.info, "Run time feature size: {0}", TagSet.GetSize() * fc[RT_FEATURE_CONTEXT].Count);

            if (fc.ContainsKey(WORDEMBEDDING_CONTEXT) == true)
                Logger.WriteLine(Logger.Level.info, "Word embedding feature size: {0}", m_WordEmbedding.GetDimension() * fc[WORDEMBEDDING_CONTEXT].Count);
        }

        void ExtractSparseFeature(int currentState, int numStates, List<string[]> features, State pState)
        {
            Dictionary<int, float> sparseFeature = new Dictionary<int, float>();
            int start = 0;
            var fc = m_FeatureConfiguration;

            //Extract TFeatures in given context window
            if (m_TFeaturizer != null)
            {
                if (fc.ContainsKey(TFEATURE_CONTEXT) == true)
                {
                    List<int> v = fc[TFEATURE_CONTEXT];
                    for (int j = 0; j < v.Count; j++)
                    {
                        int offset = TruncPosition(currentState + v[j], 0, numStates);

                        List<int> tfeatureList = m_TFeaturizer.GetFeatureIds(features, offset);
                        foreach (int featureId in tfeatureList)
                        {
                            if (m_TFeatureWeightType == TFEATURE_WEIGHT_TYPE_ENUM.BINARY)
                            {
                                sparseFeature[start + featureId] = 1;
                            }
                            else
                            {
                                if (sparseFeature.ContainsKey(start + featureId) == false)
                                {
                                    sparseFeature.Add(start + featureId, 1);
                                }
                                else
                                {
                                    sparseFeature[start + featureId]++;
                                }
                            }
                        }
                        start += m_TFeaturizer.GetFeatureSize();
                    }
                }
            }

            // Create place hold for run time feature
            // The real feature value is calculated at run time
            if (fc.ContainsKey(RT_FEATURE_CONTEXT) == true)
            {
                List<int> v = fc[RT_FEATURE_CONTEXT];
                pState.RuntimeFeatures = new PriviousLabelFeature[v.Count];
                for (int j = 0; j < v.Count; j++)
                {
                    if (v[j] < 0)
                    {
                        pState.AddRuntimeFeaturePlacehold(j, v[j], sparseFeature.Count, start);
                        sparseFeature[start] = 0; //Placehold a position
                        start += TagSet.GetSize();
                    }
                    else
                    {
                        throw new Exception("The offset of run time feature should be negative.");
                    }
                }
            }

            SparseVector spSparseFeature = pState.SparseData;
            spSparseFeature.SetDimension(m_SparseDimension);
            spSparseFeature.SetData(sparseFeature);
        }

        //Extract word embedding features from current context
        public Vector ExtractDenseFeature(int currentState, int numStates, List<string[]> features)
        {
            var fc = m_FeatureConfiguration;

            if (fc.ContainsKey(WORDEMBEDDING_CONTEXT) == true)
            {
                List<int> v = fc[WORDEMBEDDING_CONTEXT];
                if (v.Count == 1)
                {
                    string strKey = features[TruncPosition((int)currentState + v[0], 0, (int)numStates)][m_WordEmbeddingCloumn];
                    return m_WordEmbedding.GetTermVector(strKey);
                }

                CombinedVector dense = new CombinedVector();
                for (int j = 0;j < v.Count;j++)
                {
                    int offset = currentState + v[j];
                    if (offset >= 0 && offset < numStates)
                    {
                        string strKey = features[offset][m_WordEmbeddingCloumn];
                        dense.Append(m_WordEmbedding.GetTermVector(strKey));
                    }
                    else
                    {
                        dense.Append(m_WordEmbedding.m_UnkEmbedding);
                    }
                }


                return dense;
            }

            return new SingleVector();
        }


        public Sequence ExtractFeatures(Sentence sentence)
        {
            int n = sentence.TokensList.Count;
            Sequence sequence = new Sequence(n);

            //For each token, get its sparse and dense feature set according configuration and training corpus
            for (int i = 0; i < n; i++)
            {
                State state = sequence.States[i];
                ExtractSparseFeature(i, n, sentence.TokensList, state);

                state.DenseData = ExtractDenseFeature(i, n, sentence.TokensList);
            }

            return sequence;
        }
    }
}
