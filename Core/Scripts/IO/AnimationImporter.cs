using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace UniGLTF
{
    public static class AnimationImporter
    {
        private enum TangentMode
        {
            Linear,
            Constant,
            Cubicspline
        }

        private static TangentMode GetTangentMode(string interpolation)
        {
            if (interpolation == glTFAnimationTarget.Interpolations.LINEAR.ToString())
            {
                return TangentMode.Linear;
            }
            else if (interpolation == glTFAnimationTarget.Interpolations.STEP.ToString())
            {
                return TangentMode.Constant;
            }
            else if (interpolation == glTFAnimationTarget.Interpolations.CUBICSPLINE.ToString())
            {
                return TangentMode.Cubicspline;
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private static void CalculateTanget(List<Keyframe> keyframes, int current)
        {
            int back = current - 1;
            if (back < 0)
            {
                return;
            }
            if (current < keyframes.Count)
            {
                var rightTangent = (keyframes[current].value - keyframes[back].value) / (keyframes[current].time - keyframes[back].time);
                keyframes[back] = new Keyframe(keyframes[back].time, keyframes[back].value, keyframes[back].inTangent, rightTangent);

                var leftTangent = (keyframes[back].value - keyframes[current].value) / (keyframes[back].time - keyframes[current].time);
                keyframes[current] = new Keyframe(keyframes[current].time, keyframes[current].value, leftTangent, 0);
            }
        }

        public static Quaternion GetShortest(Quaternion last, Quaternion rot)
        {
            if (Quaternion.Dot(last, rot) > 0.0)
            {
                return rot;
            }
            else
            {
                return new Quaternion(-rot.x, -rot.y, -rot.z, -rot.w);
            }

        }

        public delegate float[] ReverseZ(float[] current, float[] last);
        public static void SetAnimationCurve(
            AnimationClip targetClip,
            string relativePath,
            string[] propertyNames,
            float[] input,
            float[] output,
            string interpolation,
            ReverseZ reverse)
        {
            var tangentMode = GetTangentMode(interpolation);

            var curveCount = propertyNames.Length;
            AnimationCurve[] curves = new AnimationCurve[curveCount];
            List<Keyframe>[] keyframes = new List<Keyframe>[curveCount];

            int elementNum = curveCount;
            int inputIndex = 0;
            //Quaternion�p
            float[] last = new float[curveCount];
            if (last.Length == 4)
            {
                last[3] = 1.0f;
            }
            for (inputIndex = 0; inputIndex < input.Length; ++inputIndex)
            {
                var time = input[inputIndex];
                var outputIndex = 0;
                if (tangentMode == TangentMode.Cubicspline)
                {
                    outputIndex = inputIndex * elementNum * 3;
                    var value = new float[curveCount];
                    for (int i = 0; i < value.Length; i++)
                    {
                        value[i] = output[outputIndex + elementNum + i];
                    }
                    var reversed = reverse(value, last);
                    last = reversed;
                    for (int i = 0; i < keyframes.Length; i++)
                    {
                        if (keyframes[i] == null)
                            keyframes[i] = new List<Keyframe>();
                        keyframes[i].Add(new Keyframe(
                            time,
                            reversed[i],
                            output[outputIndex + i],
                            output[outputIndex + i + elementNum * 2]));
                    }
                }
                else
                {
                    outputIndex = inputIndex * elementNum;
                    var value = new float[curveCount];
                    for (int i = 0; i < value.Length; i++)
                    {
                        value[i] = output[outputIndex + i];
                    }
                    var reversed = reverse(value, last);
                    last = reversed;

                    for (int i = 0; i < keyframes.Length; i++)
                    {
                        if (keyframes[i] == null)
                            keyframes[i] = new List<Keyframe>();
                        if (tangentMode == TangentMode.Linear)
                        {
                            keyframes[i].Add(new Keyframe(time, reversed[i], 0, 0));
                            if (keyframes[i].Count > 0)
                            {
                                CalculateTanget(keyframes[i], keyframes[i].Count - 1);
                            }
                        }
                        else if (tangentMode == TangentMode.Constant)
                            keyframes[i].Add(new Keyframe(time, reversed[i], 0, float.PositiveInfinity));
                    }
                }
            }

            for (int i = 0; i < curves.Length; i++)
            {
                curves[i] = new AnimationCurve();
                for (int j = 0; j < keyframes[i].Count; j++)
                {
                    curves[i].AddKey(keyframes[i][j]);
                }

                targetClip.SetCurve(relativePath, typeof(Transform), propertyNames[i], curves[i]);
            }
        }

        public static void ImportAnimation(ImporterContext ctx, AnimationClip clip)
        {
            for (int i = 0; i < ctx.GLTF.animations.Count; ++i)
            {
                var animation = ctx.GLTF.animations[i];
                if (string.IsNullOrEmpty(animation.name))
                {
                    animation.name = string.Format("animation:{0}", i);
                }

                foreach (var channel in animation.channels)
                {
                    var targetTransform = ctx.Nodes[channel.target.node];
                    var relativePath = targetTransform.RelativePathFrom(ctx.Root.transform);
                    switch (channel.target.path)
                    {
                        case glTFAnimationTarget.PATH_TRANSLATION:
                            {
                                var sampler = animation.samplers[channel.sampler];
                                var input = ctx.GLTF.GetArrayFromAccessor<float>(sampler.input);
                                var output = ctx.GLTF.GetArrayFromAccessorAsFloat(sampler.output);

                                AnimationImporter.SetAnimationCurve(
                                    clip,
                                    relativePath,
                                    new string[] { "localPosition.x", "localPosition.y", "localPosition.z" },
                                    input,
                                    output,
                                    sampler.interpolation,
                                    (values, last) =>
                                    {
                                        Vector3 temp = new Vector3(values[0], values[1], values[2]);
                                        return temp.ReverseZ().ToArray();
                                    }
                                    );
                            }
                            break;

                        case glTFAnimationTarget.PATH_ROTATION:
                            {
                                var sampler = animation.samplers[channel.sampler];
                                var input = ctx.GLTF.GetArrayFromAccessor<float>(sampler.input);
                                var output = ctx.GLTF.GetArrayFromAccessorAsFloat(sampler.output);

                                AnimationImporter.SetAnimationCurve(
                                    clip,
                                    relativePath,
                                    new string[] { "localRotation.x", "localRotation.y", "localRotation.z", "localRotation.w" },
                                    input,
                                    output,
                                    sampler.interpolation,
                                    (values, last) =>
                                    {
                                        Quaternion currentQuaternion = new Quaternion(values[0], values[1], values[2], values[3]);
                                        Quaternion lastQuaternion = new Quaternion(last[0], last[1], last[2], last[3]);
                                        return AnimationImporter.GetShortest(lastQuaternion, currentQuaternion.ReverseZ()).ToArray();
                                    }
                                );

                                clip.EnsureQuaternionContinuity();
                            }
                            break;

                        case glTFAnimationTarget.PATH_SCALE:
                            {
                                var sampler = animation.samplers[channel.sampler];
                                var input = ctx.GLTF.GetArrayFromAccessor<float>(sampler.input);
                                var output = ctx.GLTF.GetArrayFromAccessorAsFloat(sampler.output);

                                AnimationImporter.SetAnimationCurve(
                                    clip,
                                    relativePath,
                                    new string[] { "localScale.x", "localScale.y", "localScale.z" },
                                    input,
                                    output,
                                    sampler.interpolation,
                                    (values, last) => values);
                            }
                            break;

                        case glTFAnimationTarget.PATH_WEIGHT:
                            {
                                var node = ctx.GLTF.nodes[channel.target.node];
                                var mesh = ctx.GLTF.meshes[node.mesh];
                                for (int k = 0; k < mesh.weights.Length; ++k)
                                {
                                    //var weight = mesh.weights[k];
                                    var curve = new AnimationCurve();
                                    var sampler = animation.samplers[channel.sampler];
                                    var input = ctx.GLTF.GetArrayFromAccessor<float>(sampler.input);
                                    var output = ctx.GLTF.GetArrayFromAccessor<float>(sampler.output);
                                    for (int j = 0, l = k; j < input.Length; ++j, l += mesh.weights.Length)
                                    {
                                        curve.AddKey(input[j], output[l] * 100);
                                    }

                                    clip.SetCurve(relativePath, typeof(SkinnedMeshRenderer), "blendShape." + k, curve);
                                }
                            }
                            break;

                        default:
                            Debug.LogWarningFormat("unknown path: {0}", channel.target.path);
                            break;
                    }
                }
            }
        }

        public static void ImportAnimation(ImporterContext ctx)
        {
            // animation
            if (ctx.GLTF.animations != null && ctx.GLTF.animations.Any())
            {
                ctx.Animation = new AnimationClip();
                //ctx.Animation.name = ANIMATION_NAME;
                ctx.Animation.name = "legacy";
                ctx.Animation.ClearCurves();
                ctx.Animation.legacy = true;

                ImportAnimation(ctx, ctx.Animation);

                ctx.Animation.wrapMode = WrapMode.Loop;
                var animation = ctx.Root.AddComponent<Animation>();
                animation.AddClip(ctx.Animation, ctx.Animation.name);
                animation.clip = ctx.Animation;
            }
        }

    }
}