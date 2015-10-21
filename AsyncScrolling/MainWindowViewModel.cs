using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;

namespace AsyncScrolling
{
    public class MainWindowViewModel : ViewModelBase
    {
        #region Member Fields
        private MirrorCollection<PolicyTemplateModel> _mc;
        #endregion  // Member Fields

        #region Constructor
        public MainWindowViewModel()
        {
            if (DesignerProperties.GetIsInDesignMode(new DependencyObject())) return;

            this.CreateBigDataInfo(100);
        }
        #endregion  // Constructor

        #region Properties
        public MirrorCollection<PolicyTemplateModel> MC
        {
            get { return _mc; }
            set
            {
                _mc = value;
                this.OnPropertyChanged("MC");
            }
        }
        #endregion  // Properties

        /// <summary>
        /// MirrorCollection 초기화
        /// </summary>
        /// <param name="batchAmount">한번에 데이터를 요청할 개수(컨트롤의 보여지는 영역에서 부터 요청할 개수)</param>
        private void CreateBigDataInfo(int batchAmount)
        {
            // 100만개의 데이터가 있다는 가정하게 100만개의 데이터 키 추가
            List<long> allKeyList = new List<long>();
            for (long i = 0; i < 1000000; i++)
            {
                allKeyList.Add(i);
            }

            MirrorCollection<PolicyTemplateModel> mc = new MirrorCollection<PolicyTemplateModel>("UserNum", SyncMode.Default, batchAmount, 20);
            mc.InsertKeysTaskEnd += this.mc_InsertKeysTaskEnd;
            // 전체 데이터의 개수와 데이터 Key 추가
            mc.AddKeys(allKeyList.Count, (index) => allKeyList[index]);
            mc.ResolveFetchDataTask += this.OnDaysInfoResolveFetchDataTask;
            mc.UseAsyncCollectionChanged = false;
        }

        /// <summary>
        /// 모든 키 추가 작업 완료 이벤트 핸들러
        /// </summary>
        /// <param name="sender"></param>
        private void mc_InsertKeysTaskEnd(object sender)
        {
            // 모든 키 추가가 완료된 후 ListView컨트롤에 바인딩 데이터 표시
            MirrorCollection<PolicyTemplateModel> mc = sender as MirrorCollection<PolicyTemplateModel>;
            if (mc != null)
                MC = mc;
        }

        /// <summary>
        /// 데이터 요청 Task 이벤트 핸들러
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnDaysInfoResolveFetchDataTask(object sender, ResolveTaskEventArgs<PolicyTemplateModel> e)
        {
            Func<List<PolicyTemplateModel>> action = delegate()
            {
                return this.GetData(e.Keys);
            };

            Task<List<PolicyTemplateModel>> asyncGetData = Task<List<PolicyTemplateModel>>.Factory.StartNew(action);
            e.FetchDataTask = asyncGetData;
        }

        /// <summary>
        /// 정해진 데이터의 키 만큼 데이터 요청
        /// </summary>
        /// <param name="requestKeyList"></param>
        /// <returns></returns>
        private List<PolicyTemplateModel> GetData(IList<object> requestKeyList)
        {
            System.Threading.Thread.Sleep(3000);

            List<PolicyTemplateModel> policyTemplateList = new List<PolicyTemplateModel>();
            for (long key = requestKeyList.Cast<long>().Min(); key <= requestKeyList.Cast<long>().Max(); key++)
            {
                PolicyTemplateModel policyTemplateModel = new PolicyTemplateModel()
                {
                    UserNum = key,
                    UserID = string.Format("User ID_{0}", key),
                    UserName = System.IO.Path.GetRandomFileName(),
                    UserChecked = ((key % 2) == 0)
                };

                policyTemplateList.Add(policyTemplateModel);
            }
            
            return policyTemplateList;
        }
    }
}
