using Xunit;

namespace water2mqtt.Test
{
    public class CalcTest
    {
        [InlineData(68.69207947966561, 36.19724334064085, 285.7092535022176, 139.08814556691993, 0.3811)]
        [InlineData(157.49275022981033, 42.20704861002906, 248.16691014364528, 239.00342997277914, 0.6714)]
        [InlineData(299.1734212387239, 286.55152910244044, 97.97415595121322, 9.14779541899685, 0.0278)]
        [InlineData(23.70795955845267,
            133.8687651029021,
            331.2015718939675,
            144.76388159015403, 0.3930)]
        /*[InlineData(168.029752290219,
            93.49932798157528,
            104.26502392300938,
            8.954995268888638,
            0.0278)]*/
        [Theory]
        public void Test(double a1, double a2, double a3, double a4, double expected)
        {
            /*

trce: water2mqtt.ZennerMnk[0]
                     0 Angle 3,3963181598967367
               trce: water2mqtt.ZennerMnk[0]
                     1 Angle 355,24290076270796
               trce: water2mqtt.ZennerMnk[0]
                     2 Angle 0,29842599180784646
               trce: water2mqtt.ZennerMnk[0]
                     3 Angle 250,56911489959396
               trce: water2mqtt.ZennerMnk[0]
                     Proposed decimals: 0,7100
                            */
            var decs = ZennerMnk.AnglesToDecimals(new double[] { a1, a2, a3, a4 });

            Assert.Equal((decimal)expected, decs);
        }
    }
}

/*


trce: water2mqtt.ZennerMnk[0]
         0 Angle 168,029752290219
   trce: water2mqtt.ZennerMnk[0]
         1 Angle 93,49932798157528
   trce: water2mqtt.ZennerMnk[0]
         2 Angle 104,26502392300938
   trce: water2mqtt.ZennerMnk[0]
         3 Angle 8,954995268888638
   trce: water2mqtt.ZennerMnk[0]
         Proposed decimals: 0,0324
   info: water2mqtt.ZennerMnk[0]
         New good 1069,0324


trce: water2mqtt.ZennerMnk[0]
         0 Angle 299,1734212387239
   trce: water2mqtt.ZennerMnk[0]
         1 Angle 286,55152910244044
   trce: water2mqtt.ZennerMnk[0]
         2 Angle 97,97415595121322
   trce: water2mqtt.ZennerMnk[0]
         3 Angle 9,14779541899685
   trce: water2mqtt.ZennerMnk[0]
         Proposed decimals: 0,0278

*/

