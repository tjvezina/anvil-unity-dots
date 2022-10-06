using Unity.Collections;
using Unity.Core;
using Unity.Entities;

namespace Anvil.Unity.DOTS.Entities.Tasks
{
    /// <summary>
    /// An object generated by an <see cref="IJobConfig"/> to allow for populating job structs with the data they
    /// need in their constructors. 
    /// </summary>
    public abstract class AbstractJobData
    {
        private readonly byte m_Context;
        private readonly AbstractJobConfig m_JobConfig;
        
        /// <summary>
        /// Reference to the <see cref="World"/> this job will be running in.
        /// </summary>
        public World World { get; }
        
        /// <summary>
        /// Convenience helper to get the <see cref="TimeData"/> for delta time and other related functions.
        /// </summary>
        public ref readonly TimeData Time
        {
            get => ref World.Time;
        }


        protected AbstractJobData(World world,
                                  byte context,
                                  IJobConfig jobConfig)
        {
            World = world;
            m_Context = context;
            m_JobConfig = (AbstractJobConfig)jobConfig;
        }

        /// <summary>
        /// Gets a <see cref="CancelRequestsWriter"/> job-safe struct to use for requesting a cancel.
        /// </summary>
        /// <returns>The <see cref="CancelRequestsWriter"/></returns>
        public CancelRequestsWriter GetCancelRequestsWriter()
        {
            m_JobConfig.GetCancelRequestsDataStreamWithContext(AbstractJobConfig.Usage.Write, out CancelRequestsDataStream cancelRequestsDataStream, out byte context);
            //We want the context of who we're writing to, NOT our own context
            return cancelRequestsDataStream.CreateCancelRequestsWriter(context);
        }
        
        /// <summary>
        /// Gets a <see cref="DataStreamWriter{TInstance}"/> job-safe struct to use for writing new instances to a
        /// data stream.
        /// </summary>
        /// <typeparam name="TInstance">The type of <see cref="IEntityProxyInstance"/> to write.</typeparam>
        /// <returns>The <see cref="DataStreamWriter{TInstance}"/></returns>
        public DataStreamWriter<TInstance> GetDataStreamWriter<TInstance>()
            where TInstance : unmanaged, IEntityProxyInstance
        {
            EntityProxyDataStream<TInstance> dataStream = m_JobConfig.GetDataStream<TInstance>(AbstractJobConfig.Usage.Write);
            DataStreamWriter<TInstance> writer = dataStream.CreateDataStreamWriter(m_Context);
            return writer;
        }
        
        /// <summary>
        /// Gets a <see cref="GetDataStreamReader{TInstance}"/> job-safe struct to use for reading from a data stream.
        /// </summary>
        /// <typeparam name="TInstance">The type of <see cref="IEntityProxyInstance"/> to read.</typeparam>
        /// <returns>The <see cref="DataStreamReader{TInstance}"/></returns>
        public DataStreamReader<TInstance> GetDataStreamReader<TInstance>()
            where TInstance : unmanaged, IEntityProxyInstance
        {
            EntityProxyDataStream<TInstance> dataStream = m_JobConfig.GetDataStream<TInstance>(AbstractJobConfig.Usage.Read);
            DataStreamReader<TInstance> reader = dataStream.CreateDataStreamReader();
            return reader;
        }
        
        internal CancelRequestsReader GetCancelRequestsReader()
        {
            CancelRequestsDataStream cancelRequestsDataStream = m_JobConfig.GetCancelRequestsDataStream(AbstractJobConfig.Usage.Read);
            return cancelRequestsDataStream.CreateCancelRequestsReader();
        }

        //*************************************************************************************************************
        // NATIVE ARRAY
        //*************************************************************************************************************
        
        /// <summary>
        /// Gets a <see cref="NativeArray{T}"/> to read from in a job.
        /// </summary>
        /// <typeparam name="T">The type of struct in the array.</typeparam>
        /// <returns>The <see cref="NativeArray{T}"/></returns>
        public NativeArray<T> GetNativeArrayReadOnly<T>()
            where T : struct
        {
            return m_JobConfig.GetNativeArray<T>(AbstractJobConfig.Usage.Read);
        }

        //*************************************************************************************************************
        // ENTITY QUERY
        //*************************************************************************************************************
        
        /// <summary>
        /// Gets a <see cref="NativeArray{Entity}"/> to read from in a job from an <see cref="EntityQuery"/>
        /// </summary>
        /// <returns>The <see cref="NativeArray{Entity}"/></returns>
        public NativeArray<Entity> GetEntityNativeArrayFromQuery()
        {
            return m_JobConfig.GetEntityNativeArrayFromQuery(AbstractJobConfig.Usage.Read);
        }
        
        /// <summary>
        /// Gets a <see cref="NativeArray{T}"/> to read from in a job from an <see cref="EntityQuery"/>
        /// </summary>
        /// <typeparam name="T">The type of <see cref="IComponentData"/> in the array.</typeparam>
        /// <returns>The <see cref="NativeArray{T}"/></returns>
        public NativeArray<T> GetIComponentDataNativeArrayFromQuery<T>()
            where T : struct, IComponentData
        {
            return m_JobConfig.GetIComponentDataNativeArrayFromQuery<T>(AbstractJobConfig.Usage.Read);
        }
        
        //*************************************************************************************************************
        // CDFE
        //*************************************************************************************************************

        public CDFEReader<T> GetCDFEReader<T>()
            where T : struct, IComponentData
        {
            return m_JobConfig.GetCDFEReader<T>();
        }

        public CDFEUpdater<T> GetCDFEUpdater<T>()
            where T : struct, IComponentData
        {
            return m_JobConfig.GetCDFEUpdater<T>();
        }
    }
}
